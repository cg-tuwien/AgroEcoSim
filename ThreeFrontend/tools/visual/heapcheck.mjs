// Where does the heap actually go at production plant count?
//
// The tab is still running out of memory. `history` was the original cause and is now bounded, so
// before changing any budget it is worth measuring what ELSE is resident once one 7,700-plant
// frame is on screen: the decoded scene, the decoded-scene cache, the draw list, the GPU staging.
// A budget chosen without those numbers is a guess, and the last one was too generous by exactly
// the amount they occupy.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';

const [url, fixture] = process.argv.slice(2);
const browser = await launch(+(process.env.CDP_PORT || 25000));
let page;

try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable');
  await page.send('Page.enable'); await page.send('DOM.enable');
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });

  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(20000)]);
  await sleep(2500);

  const ev = async (e, awaitPromise = false) => {
    const r = await page.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise, timeout: 900000 });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };
  for (let i = 0; i < 60; ++i) { if (await ev(`!!window.__appstate`)) break; await sleep(1000); }

  // Collect before every reading, so what is reported is retained rather than merely allocated.
  const heap = async (label) => {
    await page.send('HeapProfiler.enable').catch(() => {});
    await page.send('HeapProfiler.collectGarbage').catch(() => {});
    await sleep(600);
    const m = await ev(`performance.memory ? {
        used: Math.round(performance.memory.usedJSHeapSize / 1048576),
        limit: Math.round(performance.memory.jsHeapSizeLimit / 1048576) } : null`);
    return { stage: label, ...m };
  };

  const stages = [];
  stages.push(await heap('empty page'));

  const chooser = new Promise(r => page.on(m => { if (m.method === 'Page.fileChooserOpened') r(m.params); }));
  await page.send('Page.setInterceptFileChooserDialog', { enabled: true });
  //A real click. Calling appstate.load() directly creates the input but the chooser only opens
  //from a user gesture, so the programmatic route waits forever for an event that never fires.
  const pt = await ev(`(() => { const b = [...document.querySelectorAll('button')].find(x => x.textContent.trim() === 'Load');
    const r = b.getBoundingClientRect(); return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }; })()`);
  for (const type of ['mousePressed', 'mouseReleased'])
    await page.send('Input.dispatchMouseEvent', { type, x: pt.x, y: pt.y, button: 'left', clickCount: 1 });
  const c = await Promise.race([chooser, sleep(15000).then(() => null)]);
  if (!c) throw new Error('no file chooser opened');
  await page.send('DOM.setFileInputFiles', { files: [fixture], backendNodeId: c.backendNodeId });
  for (let i = 0; i < 120; ++i) { await sleep(1000); if (await ev(`window.__appstate.history.length`) > 0) break; }
  await sleep(4000);

  stages.push(await heap('fixture loaded + first frame drawn'));

  const detail = await ev(`(() => { const a = window.__appstate;
      const scene = a.scene.value || [];
      return { frames: a.history.length,
               historySizeMb: +(a.historySize.value / 1048576).toFixed(1),
               plants: scene.length,
               primitives: scene.reduce((n, p) => n + p.primitives.length, 0) }; })()`);

  // The decoded-scene cache is the one nobody counts. It is budgeted in PRIMITIVES, and at this
  // scale a single frame is over a million of them - so the question is whether the budget lets it
  // hold more than one, which at hundreds of megabytes a frame is the difference that matters.
  await ev(`(() => { const a = window.__appstate; a.playPointer.value = 0; return 1; })()`);
  await sleep(3000);
  stages.push(await heap('after scrubbing (cache populated)'));

  // What the run store buys: every payload is on the server, so dropping them here is free.
  const released = await ev(`(() => { const a = window.__appstate;
      let dropped = 0, bytes = 0;
      for (const e of a.history) if (e.s) { bytes += e.s.byteLength; e.s = undefined; ++dropped; }
      a.historySize.value = 0;
      return { dropped, mb: +(bytes / 1048576).toFixed(1) }; })()`);
  stages.push(await heap('history payloads released'));

  //Separated deliberately. One number saying "a gigabyte is resident" points at nothing; knowing
  //how much the CACHE gives back and how much survives an empty scene points at the difference
  //between a budget that is too generous and something that never reclaims at all.
  await ev(`(() => { window.__clearSceneCache(); return 1; })()`);
  await sleep(1500);
  stages.push(await heap('decoded-scene cache cleared'));

  await ev(`(() => { window.__appstate.scene.value = []; return 1; })()`);
  await sleep(2500);
  stages.push(await heap('scene emptied - what is left never came back'));

  const base = stages[0].used;
  console.log(JSON.stringify({
    fixture: fixture.split('/').pop(),
    ...detail,
    released,
    heapLimitMb: stages[0].limit,
    stages: stages.map(s => ({ ...s, aboveEmpty: s.used - base })),
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0, 140)).filter(t => !/favicon/.test(t)).slice(0, 4),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
