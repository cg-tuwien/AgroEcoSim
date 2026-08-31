// Loads a fixture at production plant counts and reports whether the renderer survives it:
// console errors, GPU validation messages, JS heap, load and first-frame timings, and whether
// anything was actually drawn (via renderToPixels, since the headless canvas never composites).
import { launch, newPage, collectDiagnostics, lightingScript, sleep, shutdown } from './cdp.mjs';

const [url, fixture] = process.argv.slice(2);
const browser = await launch(+(process.env.CDP_PORT || 20500));
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
  await sleep(3000);

  const ev = async (e, awaitPromise = false, timeout = 600000) => {
    const r = await page.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise, timeout });
    if (r.exceptionDetails) return { __error: r.exceptionDetails.exception?.description || r.exceptionDetails.text };
    return r.result.value;
  };

  const heap = async () => (await ev(`performance.memory ? Math.round(performance.memory.usedJSHeapSize/1e6) : null`));
  const heapBefore = await heap();

  const chooser = new Promise(r => page.on(m => { if (m.method === 'Page.fileChooserOpened') r(m.params); }));
  await page.send('Page.setInterceptFileChooserDialog', { enabled: true });
  const pt = await ev(`(() => { const b=[...document.querySelectorAll('button')].find(x=>x.textContent.trim()==='Load');
    const r=b.getBoundingClientRect(); return {x:Math.round(r.left+r.width/2),y:Math.round(r.top+r.height/2)}; })()`);
  for (const type of ['mousePressed','mouseReleased'])
    await page.send('Input.dispatchMouseEvent', { type, x: pt.x, y: pt.y, button: 'left', clickCount: 1 });
  const ch = await Promise.race([chooser, sleep(15000).then(() => null)]);
  if (!ch) throw new Error('no file chooser');

  const t0 = Date.now();
  await page.send('DOM.setFileInputFiles', { files: [fixture], backendNodeId: ch.backendNodeId });

  // Poll until the frame slider reports a history, or we give up.
  let loadMs = null;
  for (let i = 0; i < Number(process.env.LOAD_TRIES || 90); ++i) {
    await sleep(2000);
    const s = await ev(`(() => { const el=document.querySelector('input[name=frameslider]'); return el ? el.max : null; })()`);
    if (s && +s >= 0 && !s.__error) { loadMs = Date.now() - t0; break; }
  }
  await sleep(4000);

  const seek = await ev(`(() => { const b=document.querySelector('button[title="Seek to end"]'); if(b) b.click(); return 1; })()`);
  await sleep(6000);

  //Lighting has to be set before the render, and verified after - a tier that silently did not
  //apply makes the whole measurement meaningless.
  let resolved = 'not requested';
  if (process.env.TIER !== undefined && process.env.TIER !== '') {
    await ev(lightingScript(process.env.TIER, process.env.DAYLIGHT === '1'));
    await sleep(3000);
    resolved = await ev(`(() => { const r = window.__renderer; if (!r || !r.lighting) return 'no hook';
      const l = r.lighting; return { shadows: l.shadows, ao: l.ambientOcclusion, gi: l.voxelGI }; })()`);
  }

  const stats = await ev(`(() => {
    const t = (document.getElementById('hud')||{}).innerText || '';
    const g = re => { const m = t.match(re); return m ? m[1] : null; };
    const r = window.__renderer;
    return { objects: g(/Objects:\\s*([\\d\\s,]+)/), triangles: g(/Triangles:\\s*([\\d\\s,]+)/),
             backend: r ? r.kind : 'no hook', slider: (document.querySelector('input[name=frameslider]')||{}).max };
  })()`);

  // Force a real render through the offscreen path and time it.
  const W = +(process.env.W || 320), H = +(process.env.H || 200);
  const drew = await ev(`(async () => {
    const r = window.__renderer;
    if (!r) return { error: 'no debug hook' };
    const t = performance.now();
    try {
      const W = ${W}, H = ${H};
      const px = await r.renderToPixels(W, H, 1, 1, r.getCameraPose());
      const d = px.data; let nonClear = 0;
      for (let i = 0; i < d.length; i += 4)
        if (Math.abs(d[i]-250) > 6 || Math.abs(d[i+1]-250) > 6 || Math.abs(d[i+2]-250) > 6) ++nonClear;
      //Hand the raw pixels back as base64 so the caller can save a real image; this is the
      //renderer's own output, bypassing the export UI entirely, which is how a failing export
      //gets separated from a failing render.
      let rgba = '';
      if (${process.env.DUMP ? 'true' : 'false'}) {
        const chunk = 8192;
        for (let i = 0; i < d.length; i += chunk) rgba += String.fromCharCode.apply(null, d.subarray(i, i + chunk));
        rgba = btoa(rgba);
      }
      return { ms: Math.round(performance.now() - t), nonClearPixels: nonClear, total: d.length/4,
               width: px.width, height: px.height, rgba };
    } catch (e) { return { error: String(e).slice(0, 300), ms: Math.round(performance.now() - t) }; }
  })()`, true);

  if (drew && drew.rgba && process.env.DUMP) {
    const { writeFileSync } = await import('node:fs');
    writeFileSync(process.env.DUMP + '.rgba', Buffer.from(drew.rgba, 'base64'));
    writeFileSync(process.env.DUMP + '.json', JSON.stringify({ width: drew.width, height: drew.height }));
    delete drew.rgba;
  }

  console.log(JSON.stringify({
    fixture: fixture.split('/').pop(),
    loadMs, heapBeforeMB: heapBefore, heapAfterMB: await heap(),
    tier: process.env.TIER ?? null, resolved, stats, drew,
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0, 220)).slice(0, 6),
    warnings: logs.filter(l => l.level === 'warning' && !/GL Driver|external Instance/.test(l.text))
      .map(l => l.text.split('\n')[0].slice(0, 220)).slice(0, 4),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
