// What the playback machine looks like the moment a run ends, with no reload involved.
//
// The recovery harness found the playhead one past the end of a finished run and the "Previous
// frame" button disabled. That is either a pre-existing property of the playback state machine or
// something the reattach path introduced, and the two call for opposite responses - so measure the
// plain case rather than reasoning about it.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';

const [url, backend] = process.argv.slice(2);
const browser = await launch(+(process.env.CDP_PORT || 24800));
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

  await ev(page, `(() => { const a = window.__appstate;
      a.totalHours.value = ${process.env.TOTAL_HOURS || 400}; a.hoursPerTick.value = 4;
      a.randomize.value = false; a.initNumber.value = 42; return 1; })()`);
  await ev(page, `(() => { document.querySelector('button[title=Simulate]').click(); return 1; })()`);

  let runId = '';
  for (let i = 0; i < 60; ++i) { await sleep(500); runId = await ev(page, `window.__appstate.runId.value`); if (runId) break; }
  if (!runId) throw new Error('no run id announced');

  // The server's status is the authority on whether the run is over; the client's `computing`
  // flag is a consequence of a message and can lag or, after a reload, never have been set.
  for (let i = 0; i < 400; ++i) {
    await sleep(1000);
    const st = await ev(page, `(async () => { const r = await fetch('${backend}/Simulation/run/${runId}');
        return r.ok ? (await r.json()).status : 'gone'; })()`, true);
    if (st !== 'running') break;
  }
  await sleep(4000);

  const state = await ev(page, `(() => { const a = window.__appstate;
      const prev = document.querySelector('button[title="Previous frame"]');
      return { frames: a.history.length, pointer: a.playPointer.value, playing: a.playing.value,
               computing: a.computing.value,
               pointerInRange: a.playPointer.value >= 0 && a.playPointer.value < a.history.length,
               prevDisabled: prev ? prev.disabled : null }; })()`);

  console.log(JSON.stringify({ scenario: 'run to completion, NO reload', ...state,
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0, 140)).filter(t => !/favicon/.test(t)).slice(0, 3) }, null, 1));
} finally { page?.close(); await shutdown(browser); }
