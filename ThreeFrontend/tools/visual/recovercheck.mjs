// Reloading the tab in the middle of a run used to lose the run.
//
// The frames were on disk the whole time and `GET /run/{id}/index` served the timeline, but
// nothing in the client ever called it - and the socket is not opened on page load either, so
// nothing reattached. The run went on computing for an audience that had gone, until the
// unwatched grace period stopped it, while the tab showed an empty transport bar and no hint
// that a run had ever existed.
//
// This drives the real thing: run, reload, and then ask the reloaded page what it has.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';

const [url, backend] = process.argv.slice(2);
const browser = await launch(+(process.env.CDP_PORT || 24700));
let page;

const ev = async (p, e, awaitPromise = false) => {
  const r = await p.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise, timeout: 600000 });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  return r.result.value;
};

async function navigate(p, to) {
  const loaded = new Promise(r => p.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await p.send('Page.navigate', { url: to });
  await Promise.race([loaded, sleep(20000)]);
  for (let i = 0; i < 60; ++i) { if (await ev(p, `!!window.__appstate`)) break; await sleep(1000); }
  await sleep(1500);
}

try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable'); await page.send('Page.enable');
  await navigate(page, url);

  // --- 1. a short run, so there is something to lose -----------------------------------------
  await ev(page, `(() => { const a = window.__appstate;
      a.totalHours.value = ${process.env.TOTAL_HOURS || 1200}; a.hoursPerTick.value = 4;
      a.randomize.value = false; a.initNumber.value = 42;
      return 1; })()`);
  await ev(page, `(() => { document.querySelector('button[title=Simulate]').click(); return 1; })()`);

  let runId = '';
  for (let i = 0; i < 60; ++i) {
    await sleep(500);
    runId = await ev(page, `window.__appstate.runId.value`);
    if (runId) break;
  }
  if (!runId) throw new Error('no run id was announced');

  // Reload MID-RUN - the case that used to orphan the run entirely. Waiting for the client to
  // hold a few frames is not enough on its own: the check below asserts the run is still
  // computing at the moment of the reload, so a run that finished early cannot masquerade as a
  // successful mid-run reattach.
  for (let i = 0; i < 300; ++i) {
    await sleep(1000);
    if (await ev(page, `window.__appstate.history.length`) >= 8) break;
  }
  const stillComputing = await ev(page, `window.__appstate.computing.value`);
  const before = await ev(page, `(() => { const a = window.__appstate; return {
      frames: a.history.length, count: a.historyCount.value, bytes: a.historySize.value,
      runId: a.runId.value, stored: sessionStorage.getItem('agro.runId') }; })()`);
  before.stillComputing = stillComputing;

  // --- 2. the reload ---------------------------------------------------------------------------
  await navigate(page, url);
  // Recovery is a fetch plus a socket open; give it room.
  let after = null;
  for (let i = 0; i < 40; ++i) {
    await sleep(1000);
    after = await ev(page, `(() => { const a = window.__appstate; return {
        frames: a.history.length, count: a.historyCount.value, bytes: a.historySize.value,
        evicted: a.evictedFrames.value, runId: a.runId.value,
        pointer: a.playPointer.value }; })()`);
    if (after.frames > 0) break;
  }

  // --- 2b. let the reattached run finish, then check the timeline is COMPLETE -----------------
  //
  // The wire is lazy and the store is not: the server exports every step while the client is sent
  // only the frames it asks for. A run that stored 60 frames and streamed 20 used to leave the
  // other 40 on disk and unreachable. After the run ends the client should hold every stored step.
  //
  // Wait on the SERVER's status, not on the client's `computing` flag. That flag is false from
  // the moment a reloaded page loads, so a loop waiting for it to go false returns instantly and
  // samples a run that is still going - which is not a measurement of anything. The first version
  // of this check did exactly that and reported a failure that was its own.
  let serverStatus = 'running';
  for (let i = 0; i < 400; ++i) {
    await sleep(1000);
    serverStatus = await ev(page, `(async () => {
        const r = await fetch('${backend}/Simulation/run/${runId}');
        return r.ok ? (await r.json()).status : 'gone'; })()`, true);
    if (serverStatus !== 'running') break;
  }
  //The completion sync is driven by the Result message, which lands a moment after the status
  //flips. Give it that moment rather than racing it.
  let settled = null;
  for (let i = 0; i < 30; ++i) {
    await sleep(1000);
    settled = await ev(page, `(() => { const a = window.__appstate; return {
        computing: a.computing.value, frames: a.history.length, count: a.historyCount.value,
        evicted: a.evictedFrames.value, bytes: a.historySize.value }; })()`);
    if (!settled.computing) break;
  }

  // --- 3. is the recovered timeline USABLE, not just present? ----------------------------------
  const usable = await ev(page, `(async () => {
      const a = window.__appstate;
      if (a.history.length === 0) return { ok: false, why: 'nothing recovered' };
      // Every entry must have a step and NO payload: that is the point - the timeline costs
      // 8 bytes a frame while the payloads stay on the server.
      const withPayload = a.history.filter(e => e.s).length;
      const steps = a.history.map(e => e.t);
      const monotonic = steps.every((t, i) => i === 0 || t > steps[i - 1]);

      // Scrub to the middle and make the app fetch that frame back through its own path.
      const mid = Math.floor(a.history.length / 2);
      const t0 = performance.now();
      const scene = await window.__sceneAt(mid);
      const ms = performance.now() - t0;
      return { ok: true, withPayload, monotonic, firstStep: steps[0], lastStep: steps[steps.length - 1],
               midIndex: mid, midPlants: scene.length,
               midPrimitives: scene.reduce((n, p) => n + p.primitives.length, 0),
               fetchMs: Math.round(ms) };
    })()`, true);

  // --- 4. and does the transport bar believe it? -----------------------------------------------
  //
  // What Stage 2 actually promises the transport bar: a slider spanning every stored frame and a
  // seek control that is live. NOT "nothing is disabled" - at the end of a run the forward
  // controls are correctly disabled, and `Previous frame` is disabled too because a finished run
  // is left in PlayState.ForwardWaiting with the playhead one past the last frame. That is
  // pre-existing (tools/visual/playstate.mjs measures it on a run with no reload at all: 100
  // frames, pointer 100, playing 3) and belongs to the playback machine, not to persistence.
  const transport = await ev(page, `(() => {
      const seek = document.querySelector('input[name=frameslider]');
      const btns = [...document.querySelectorAll('button[title="Seek to start"], button[title="Previous frame"]')];
      return { pointer: window.__appstate.playPointer.value,
               sliderMax: seek ? +seek.max : null, sliderDisabled: seek ? seek.disabled : null,
               buttons: btns.map(b => ({ title: b.title, disabled: b.disabled })) };
    })()`);

  const serverSays = await ev(page, `(async () => {
      const r = await fetch('${backend}/Simulation/run/' + '${runId}');
      return r.ok ? await r.json() : { error: r.status };
    })()`, true);

  const recovered = after && after.frames > 0;
  const complete = settled && serverSays.storedFrames === settled.frames;
  console.log(JSON.stringify({
    //A reload that happened after the run ended proves recovery but says nothing about
    //reattaching to a LIVE run, so it is not allowed to pass as one.
    verdict: recovered && usable.ok && usable.monotonic && usable.midPrimitives > 0
             && after.runId === before.runId && complete
             && !transport.sliderDisabled && transport.sliderMax === settled.frames - 1
             && !transport.buttons.find(b => b.title === 'Seek to start').disabled
             && before.stillComputing
      ? 'RECOVERED' : 'FAILED',
    reloadedMidRun: before.stillComputing,
    timelineComplete: complete,
    serverStatus,
    clientFrames: settled ? settled.frames : null,
    beforeReload: before,
    afterReload: after,
    afterFinish: settled,
    recoveredTimeline: usable,
    transport,
    serverSays: { status: serverSays.status, storedFrames: serverSays.storedFrames,
                  totalSteps: serverSays.totalSteps, bytesOnDisk: serverSays.bytesOnDisk },
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0, 140))
      .filter(t => !/CONNECTION_REFUSED|favicon/.test(t)).slice(0, 4),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
