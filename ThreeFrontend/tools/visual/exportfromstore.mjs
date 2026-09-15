// Exporting the animation of a run that is on disk, without holding the run in memory.
//
// The tab is most likely to die at exactly the moment an export starts: a long run has just
// finished, the history is at its budget, and one 7,700-plant frame on screen already costs about
// 1.1 GB on its own. But every frame is in the run store, and the export reads them one at a time
// - so the copy in memory buys nothing there. This proves the export gives them back first, keeps
// the heap flat while it runs, and still produces a complete video.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';
import { writeFileSync } from 'node:fs';

const [url, backend, outPath] = process.argv.slice(2);
const PLANTS = +(process.env.PLANTS || 60);
const browser = await launch(+(process.env.CDP_PORT || 25100));
let page;

const ev = async (p, e, awaitPromise = false) => {
  const r = await p.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise, timeout: 900000 });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  return r.result.value;
};

try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable');
  await page.send('Page.enable'); await page.send('DOM.enable');
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(20000)]);
  for (let i = 0; i < 60; ++i) { if (await ev(page, `!!window.__appstate`)) break; await sleep(1000); }
  await sleep(1500);

  const heap = async () => {
    await page.send('HeapProfiler.enable').catch(() => {});
    await page.send('HeapProfiler.collectGarbage').catch(() => {});
    await sleep(400);
    return await ev(page, `Math.round(performance.memory.usedJSHeapSize / 1048576)`);
  };

  // A real run, so there is a run id and a store behind it.
  await ev(page, `(() => { const a = window.__appstate;
      a.totalHours.value = ${process.env.TOTAL_HOURS || 800}; a.hoursPerTick.value = 4;
      a.randomize.value = false; a.initNumber.value = 42;
      const Seed = a.seeds.value[0].constructor, species = a.species.value[0].name.peek();
      const n = ${PLANTS}, side = Math.ceil(Math.sqrt(n)), out = [];
      for (let i = 0; i < n; ++i)
        out.push(new Seed(species, 0.3 + (i % side) * (9 / side), -0.01, 0.3 + Math.floor(i / side) * (9 / side), 0, false));
      a.seeds.value = out; return 1; })()`);
  await ev(page, `(() => { document.querySelector('button[title=Simulate]').click(); return 1; })()`);

  let runId = '';
  for (let i = 0; i < 120; ++i) { await sleep(500); runId = await ev(page, `window.__appstate.runId.value`); if (runId) break; }
  if (!runId) throw new Error('no run id announced');
  for (let i = 0; i < 600; ++i) {
    await sleep(2000);
    const st = await ev(page, `(async () => { const r = await fetch('${backend}/Simulation/run/${runId}');
        return r.ok ? (await r.json()).status : 'gone'; })()`, true);
    if (st !== 'running') break;
  }
  await sleep(4000);

  const before = await ev(page, `(() => { const a = window.__appstate; return {
      frames: a.history.length, heldPayloads: a.history.filter(e => e.s).length,
      historyMb: +(a.historySize.value / 1048576).toFixed(1),
      budgetMb: +(a.historyBudget.value / 1048576).toFixed(0) }; })()`);
  const heapBeforeExport = await heap();

  //Capture the produced file. The panel's "Saved N MB" text is not a reliable completion signal -
  //animate.mjs reports done:false on exports that demonstrably produced a file - so the bytes are
  //the only honest evidence that the export finished.
  await ev(page, `(() => { const orig = URL.createObjectURL.bind(URL);
      URL.createObjectURL = b => { window.__out = b; return orig(b); }; return 1; })()`);

  // Export the whole run, driven through the panel exactly as a person would - there is no
  // scripted export entry point, and inventing one would test a path the app does not use.
  const started = Date.now();
  const clicked = await ev(page, `(() => { const b = document.querySelector('button[name=renderAnimation]');
      if (!b) return 'missing'; if (b.disabled) return 'disabled'; b.click(); return 'clicked'; })()`);
  if (clicked !== 'clicked') throw new Error(`export button was ${clicked}`);

  let peak = 0, status = null;
  for (let i = 0; i < 900; ++i) {
    await sleep(2000);
    peak = Math.max(peak, await ev(page, `Math.round(performance.memory.usedJSHeapSize / 1048576)`));
    status = await ev(page, `(() => { const b = document.querySelector('button[name=renderAnimation]');
        return { busy: b ? b.disabled : null,
                 progress: (document.body.innerText.match(/(\\d+)\\/(\\d+) . \\w+/) || [])[0] || null,
                 done: /Saved [\\d.]+ MB/.test(document.body.innerText) }; })()`);
    if (status.done || status.busy === false) break;
  }

  const after = await ev(page, `(() => { const a = window.__appstate; return {
      frames: a.history.length, heldPayloads: a.history.filter(e => e.s).length,
      evicted: a.evictedFrames.value,
      historyMb: +(a.historySize.value / 1048576).toFixed(1) }; })()`);
  const heapAfter = await heap();
  const produced = await ev(page, `(async () => window.__out ? (await window.__out.arrayBuffer()).byteLength : 0)()`, true);

  console.log(JSON.stringify({
    runId: runId.slice(0, 8),
    plants: PLANTS,
    beforeExport: { ...before, heapMb: heapBeforeExport },
    afterExport: { ...after, heapMb: heapAfter, peakMb: peak },
    // The point of the whole thing: the export must not need the run in memory.
    releasedForExport: before.heldPayloads - after.heldPayloads,
    exportSeconds: Math.round((Date.now() - started) / 1000),
    exportStatus: status,
    producedBytes: produced,
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.slice(0, 700)).filter(t => !/favicon/.test(t)).slice(0, 3),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
