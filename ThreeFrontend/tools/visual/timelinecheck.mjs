// Does the timeline keep up with the run, or only with what the browser managed to draw?
//
// The server exports and stores every step. The wire sends a frame only when the browser asks for
// one, and at several thousand plants the browser asks rarely - so the slider used to span the
// handful of frames that had arrived while the counter below it read 550/1080. Same run, two
// numbers, and the honest one was the small print.
//
// Needs enough plants that the browser genuinely falls behind; with one plant it keeps up and the
// gap this measures never opens.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';

const [url, backend] = process.argv.slice(2);
const PLANTS = +(process.env.PLANTS || 300);
const browser = await launch(+(process.env.CDP_PORT || 24900));
let page;

const ev = async (p, e, awaitPromise = false) => {
  const r = await p.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise, timeout: 600000 });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  return r.result.value;
};

try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable'); await page.send('Page.enable');
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(20000)]);
  for (let i = 0; i < 60; ++i) { if (await ev(page, `!!window.__appstate`)) break; await sleep(1000); }
  await sleep(1500);

  // Placed explicitly, not via pushSeedRaster - that helper picks a random species per position,
  // which is fine for a load test but makes the run irreproducible for no benefit.
  const planted = await ev(page, `(() => { const a = window.__appstate;
      a.totalHours.value = ${process.env.TOTAL_HOURS || 2400}; a.hoursPerTick.value = 4;
      a.randomize.value = false; a.initNumber.value = 42;
      const Seed = a.seeds.value.length ? a.seeds.value[0].constructor : null;
      if (!Seed) return -1;
      const species = a.species.value[0].name.peek();
      const n = ${PLANTS}, side = Math.ceil(Math.sqrt(n)), out = [];
      for (let i = 0; i < n; ++i)
        out.push(new Seed(species, 0.3 + (i % side) * (9 / side), -0.01, 0.3 + Math.floor(i / side) * (9 / side), 0, false));
      a.seeds.value = out;
      return a.seeds.value.length; })()`);
  if (planted !== PLANTS) throw new Error(`wanted ${PLANTS} plants, planted ${planted}`);

  await ev(page, `(() => { document.querySelector('button[title=Simulate]').click(); return 1; })()`);
  let runId = '';
  for (let i = 0; i < 120; ++i) { await sleep(500); runId = await ev(page, `window.__appstate.runId.value`); if (runId) break; }
  if (!runId) throw new Error('no run id announced');

  // Force the condition this exists for.
  //
  // At 300 small plants the browser keeps up, every frame arrives on the wire, and the sync has
  // nothing to do - so simply watching the gap "passes" without exercising anything. Truncating
  // the history mid-run reproduces what a large stand does naturally: the slider is suddenly far
  // behind the run, and the only way back is the index.
  for (let i = 0; i < 120; ++i) { await sleep(1000); if (await ev(page, `window.__appstate.simStep.value`) >= 30) break; }
  const truncated = await ev(page, `(() => { const a = window.__appstate;
      a.history.length = 5;
      a.historyCount.value = 5;
      a.historySize.value = 0;
      a.playPointer.value = 4;
      return { frames: a.history.length, simStep: a.simStep.value }; })()`);

  // Sample the gap between what the server has computed and what the slider spans, while it runs.
  const samples = [];
  for (let i = 0; i < 60; ++i) {
    await sleep(2000);
    const s = await ev(page, `(() => { const a = window.__appstate;
        const slider = document.querySelector('input[name=frameslider]');
        const end = document.querySelector('button[title="Seek to end"]');
        return { simStep: a.simStep.value, simLength: a.simLength.value,
                 frames: a.history.length, sliderMax: slider ? +slider.max : -1,
                 withoutPayload: a.history.filter(e => !e.s).length,
                 //The reported symptom, sampled rather than described: the forward controls used
                 //to flicker because their guard compared the playhead against a history length
                 //that the live sync keeps moving.
                 seekEndDisabled: end ? end.disabled : null,
                 computing: a.computing.value }; })()`);
    if (s.simStep > 8) samples.push(s);
    if (!s.computing && s.simStep > 0) break;
    if (samples.length >= 12) break;
  }
  if (!samples.length) throw new Error('the run never produced a sample');

  // The gap that matters: simulated steps the server has done, minus frames the slider offers.
  const gaps = samples.map(s => s.simStep - s.frames);
  const worst = Math.max(...gaps);
  const mid = samples[Math.floor(samples.length / 2)];

  // Frames present with no payload are precisely the ones taken from the index rather than
  // received on the wire. If none appear, the history was never actually behind and the check
  // proved nothing - so require them.
  const recovered = samples.some(s => s.withoutPayload > 0);
  //Count how often the button changes state while the run streams. Anything above zero is the
  //flicker; it should be disabled for the whole time the tab is following the live edge.
  const live = samples.filter(s => s.computing);
  const flickers = live.filter((s, i) => i > 0 && s.seekEndDisabled !== live[i - 1].seekEndDisabled).length;
  console.log(JSON.stringify({
    // A slider that spans the computed run, not just the drawn one. The threshold is generous:
    // the sync is throttled to one request every two seconds, so a few steps of lag is by design.
    seekEndFlickers: flickers,
    seekEndStatesWhileStreaming: [...new Set(live.map(s => s.seekEndDisabled))],
    verdict: flickers > 0 ? 'SEEK-TO-END FLICKERS'
      : worst <= 40 && mid.sliderMax === mid.frames - 1 && recovered
      ? 'TIMELINE TRACKS THE RUN'
      : !recovered ? 'INCONCLUSIVE - the history was never behind' : 'TIMELINE LAGS',
    plants: PLANTS,
    truncatedMidRunTo: truncated,
    recoveredFromIndex: recovered,
    worstGapSteps: worst,
    gaps,
    // Frames present with no payload are exactly the ones recovered from the run index rather
    // than received on the wire - the mechanism working, visible.
    midSample: mid,
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0, 140)).filter(t => !/favicon/.test(t)).slice(0, 4),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
