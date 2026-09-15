// Loads a saved session, waits for the background compressor to drain, and reports how much
// memory the stored frames actually give back.
//
// The number that matters is stored-vs-raw. Real V7 scene data is float-heavy and deflates about
// 1.7x - anyone expecting more should see the measurement rather than the hope.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';

const [url, fixture] = process.argv.slice(2);
const browser = await launch(+(process.env.CDP_PORT || 23400));
let page;
try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable');
  await page.send('Page.enable'); await page.send('DOM.enable');
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(15000)]);
  await sleep(2500);

  const ev = async (e, awaitPromise = false) => {
    const r = await page.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };

  const chooser = new Promise(r => page.on(m => { if (m.method === 'Page.fileChooserOpened') r(m.params); }));
  await page.send('Page.setInterceptFileChooserDialog', { enabled: true });
  const pt = await ev(`(() => { const b=[...document.querySelectorAll('button')].find(x=>x.textContent.trim()==='Load');
    const r=b.getBoundingClientRect(); return {x:Math.round(r.left+r.width/2),y:Math.round(r.top+r.height/2)}; })()`);
  for (const type of ['mousePressed','mouseReleased'])
    await page.send('Input.dispatchMouseEvent', { type, x: pt.x, y: pt.y, button: 'left', clickCount: 1 });
  const ch = await Promise.race([chooser, sleep(10000).then(()=>null)]);
  await page.send('DOM.setFileInputFiles', { files: [fixture], backendNodeId: ch.backendNodeId });
  await sleep(6000);

  const stats = () => ev(`(() => { const h = window.__appstate.history;
      let stored = 0, plain = 0, packed = 0;
      for (const e of h) { stored += e.s.byteLength; if (e.plain) ++plain; else ++packed; }
      return { frames: h.length, plain, packed, stored, historySize: window.__appstate.historySize.value }; })()`);

  const before = await stats();
  //A loaded session arrives entirely uncompressed; nudge the compressor the way a running
  //simulation would, then let the idle queue drain.
  await ev(`(() => { const s = document.querySelector('button[title="Seek to end"]'); if (s) s.click(); return 1; })()`);
  for (let i = 0; i < 40; ++i) {
    await sleep(1000);
    const now = await stats();
    if (now.packed >= now.frames - 8) break;
  }
  const after = await stats();

  // Prove the frames still decode after being packed away.
  const readback = await ev(`(async () => {
      const h = window.__appstate.history;
      const last = h.length - 1, mid = Math.floor(h.length / 2);
      const a = await window.__sceneAt(mid), b = await window.__sceneAt(last);
      return { midPrims: a.reduce((n,p)=>n+p.primitives.length,0),
               lastPrims: b.reduce((n,p)=>n+p.primitives.length,0),
               midWasPacked: !h[mid].plain }; })()`, true);

  console.log(JSON.stringify({ before, after, readback,
    ratio: after.stored ? +(before.stored / after.stored).toFixed(2) : null,
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0,140))
      .filter(t => !/CONNECTION_REFUSED|Failed to fetch/.test(t)).slice(0,3) }, null, 1));
} finally { page?.close(); await shutdown(browser); }
