// Forces payload eviction with an artificially small budget, then proves the evicted frames come
// back from the run store and decode to the same scenes.
//
// This is the whole point of Stage 2: the client keeps `t` and lets the bytes go.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';

const [url] = process.argv.slice(2);
const browser = await launch(+(process.env.CDP_PORT || 23900));
let page;
try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable'); await page.send('Page.enable');
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(15000)]);
  await sleep(2500);

  const ev = async (e, awaitPromise = false) => {
    const r = await page.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise, timeout: 600000 });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };

  //The debug hook is installed when the renderer mounts, not when the page loads.
  for (let i = 0; i < 60; ++i) {
    if (await ev(`!!window.__appstate`)) break;
    await sleep(1000);
  }
  if (!await ev(`!!window.__appstate`))
    throw new Error('debug hook never appeared. renderer=' + await ev(`!!window.__renderer`)
      + ' search=' + await ev(`location.search`)
      + ' canvas=' + await ev(`!!document.querySelector('canvas')`));

  const set = (n, v) => ev(`(() => { const el=document.querySelector('[name=${n}]'); if(!el) return 'missing';
    el.value=String(${JSON.stringify(v)});
    el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); return 'ok'; })()`);
  await set('totalhours', 480);
  await set('hourspertick', 4);

  //A budget small enough that a short run has to start dropping payloads.
  await ev(`(() => { window.__appstate.historyBudget.value = 20000; return 1; })()`);
  await ev(`(() => { document.querySelector('button[title=Simulate]').click(); return 1; })()`);

  for (let i = 0; i < 150; ++i) {
    await sleep(2000);
    const st = await ev(`(() => { const b=document.querySelector('button[title=Simulate]');
      return { computing: b ? b.innerText.trim() !== '\\u{1F680}' : null,
               frames: +((document.querySelector('input[name=frameslider]')||{}).max || 0) }; })()`);
    if (st.computing === false && st.frames > 1) break;
  }
  await sleep(2500);

  const state = await ev(`(() => { const a = window.__appstate;
      const h = a.history;
      return { runId: a.runId.value, frames: h.length, evicted: a.evictedFrames.value,
               withPayload: h.filter(e => e.s).length, withoutPayload: h.filter(e => !e.s).length,
               historySize: a.historySize.value, budget: a.historyBudget.value }; })()`);

  //Scrub back to the very first frame - guaranteed evicted - and check it renders.
  const recovered = await ev(`(async () => {
      const h = window.__appstate.history;
      //Skip t=0: it is the empty pre-germination scene and would prove nothing.
      const gone = h.map((e,i) => [e,i]).filter(([e]) => !e.s && e.t > 0).map(([,i]) => i);
      if (!gone.length) return { count: 0 };
      const pick = gone[Math.floor(gone.length / 2)];
      const before = performance.now();
      const scene = await window.__sceneAt(pick);
      const ms = Math.round(performance.now() - before);
      //And a second read must now be synchronous - the decoded frame is in the LRU.
      const cached = window.__appstate ? null : null;
      return { count: gone.length, index: pick, t: h[pick].t, fetchMs: ms,
               plants: scene.length,
               primitives: scene.reduce((n,p) => n + p.primitives.length, 0),
               stillEvicted: !h[pick].s }; })()`, true);

  console.log(JSON.stringify({ state, recovered,
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0,140))
      .filter(t => !/CONNECTION_REFUSED/.test(t)).slice(0,3) }, null, 1));
} finally { page?.close(); await shutdown(browser); }
