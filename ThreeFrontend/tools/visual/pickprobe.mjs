// Regression probe for the frame uniform being poisoned by picking.
//
// The depth+normal prepass draws through the shared frame uniform. The pick pass writes a 1x1
// skewed gluPickMatrix into that same buffer, so if it is left there the prepass renders from
// it and shadows and occlusion follow the cursor. Offscreen stills cannot catch this - they
// write their own uniform first - so the invariant is checked directly: after a hover, the
// uniform must still hold the camera's matrices.
import { launch, newPage, sleep, shutdown, lightingScript } from './cdp.mjs';

const [url, sessionPath] = process.argv.slice(2);
const browser = await launch(+(process.env.CDP_PORT || 9799));
let page;
try {
  page = await newPage(browser.port); await page.ready;
  await page.send('Runtime.enable'); await page.send('Page.enable'); await page.send('DOM.enable');
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(15000)]); await sleep(3500);

  const ev = async (e, ap = false) => {
    const r = await page.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise: ap });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };

  const chooser = new Promise(r => page.on(m => { if (m.method === 'Page.fileChooserOpened') r(m.params); }));
  await page.send('Page.setInterceptFileChooserDialog', { enabled: true });
  const pt = await ev(`(() => { const b=Array.from(document.querySelectorAll('button')).find(x=>x.textContent.trim()==='Load'); const r=b.getBoundingClientRect(); return {x:Math.round(r.left+r.width/2),y:Math.round(r.top+r.height/2)}; })()`);
  for (const type of ['mousePressed','mouseReleased'])
    await page.send('Input.dispatchMouseEvent', { type, x: pt.x, y: pt.y, button: 'left', clickCount: 1 });
  const ch = await Promise.race([chooser, sleep(8000).then(() => null)]);
  await page.send('DOM.setFileInputFiles', { files: [sessionPath], backendNodeId: ch.backendNodeId });
  await sleep(6000);
  await ev(`(() => { const b=document.querySelector('button[title="Seek to end"]'); if(b) b.click(); return 1; })()`);
  await sleep(3000);
  await ev(lightingScript(process.env.TIER || '2'));
  await sleep(2500);

  //The camera's own view-projection, for comparison.
  const expected = await ev(`(() => {
    const r = window.__renderer;
    const t = r.targets;
    return Array.from(r.camera.viewProjection(t.width / Math.max(1, t.height))).map(v => +v.toFixed(5));
  })()`);

  const canvas = await ev(`(() => { const c = document.querySelector('canvas'); const b = c.getBoundingClientRect();
    return { x: Math.round(b.left + b.width * 0.55), y: Math.round(b.top + b.height * 0.55) }; })()`);
  //A hover, no buttons: exactly what the user was doing.
  await page.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: canvas.x, y: canvas.y });
  await sleep(1200);

  const after = await ev(`(() => Array.from(window.__renderer.frameData.slice(0, 16)).map(v => +v.toFixed(5)))()`);
  const drift = expected.map((v, i) => Math.abs(v - after[i])).reduce((a, b) => Math.max(a, b), 0);

  console.log(JSON.stringify({
    picking: await ev(`(() => { const el = document.querySelector('[name=performPicking]'); return el ? el.checked : 'missing'; })()`),
    maxDrift: +drift.toFixed(6),
    ok: drift < 1e-4,
    expectedHead: expected.slice(0, 4), afterHead: after.slice(0, 4),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
