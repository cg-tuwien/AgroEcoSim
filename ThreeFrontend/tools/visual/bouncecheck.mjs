// Measures the second bounce in the density field itself (plan phase 16).
//
// Counting screen pixels is a blunt way to test a bounce: it only sees the fraction of the
// frame the canopy covers. This reads the coarsest mip of the radiance texture, which - because
// the mip chain is an arithmetic mean of a sigma-premultiplied quantity - is exactly the volume
// average of the field. It reports the ratio between one bounce and two.
import { launch, newPage, collectDiagnostics, lightingScript, sleep, shutdown } from './cdp.mjs';

const [url, sessionPath] = process.argv.slice(2);

const browser = await launch(+(process.env.CDP_PORT || 9770));
let page;
try {
  page = await newPage(browser.port);
  await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable');
  await page.send('Page.enable'); await page.send('DOM.enable');
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });

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
  const pt = await ev(`(() => { const b=Array.from(document.querySelectorAll('button')).find(x=>x.textContent.trim()==='Load'); const r=b.getBoundingClientRect(); return {x:Math.round(r.left+r.width/2),y:Math.round(r.top+r.height/2)}; })()`);
  for (const type of ['mousePressed','mouseReleased'])
    await page.send('Input.dispatchMouseEvent', { type, x: pt.x, y: pt.y, button: 'left', clickCount: 1 });
  const ch = await Promise.race([chooser, sleep(8000).then(()=>null)]);
  if (!ch) throw new Error('no file chooser');
  await page.send('DOM.setFileInputFiles', { files: [sessionPath], backendNodeId: ch.backendNodeId });
  await sleep(6000);
  await ev(`(() => { const b=document.querySelector('button[title="Seek to end"]'); if(b) b.click(); return 1; })()`);
  await sleep(3000);

  const setTier = async t => {
    await ev(lightingScript(t));
    await sleep(5000);
    return ev(`(async () => {
      const r = window.__renderer;
      if (!r || !r.readVoxelMeanRadiance) return null;
      const m = await r.readVoxelMeanRadiance();
      const n = r.readVoxelOccupancy ? await r.readVoxelOccupancy() : null;
      return m ? { r: m.r, g: m.g, b: m.b, sigma: m.sigma, occupied: n } : null;
    })()`, true);
  };

  const one = await setTier(3);
  const two = await setTier(4);
  const lum = m => m ? (m.r + m.g + m.b) / 3 : 0;
  const gain = lum(one) > 0 ? lum(two) / lum(one) : null;

  console.log(JSON.stringify({
    oneBounce: one, twoBounces: two,
    meanRadianceGain: gain === null ? null : +gain.toFixed(4),
    percentBrighter: gain === null ? null : +((gain - 1) * 100).toFixed(1),
    errors: logs.filter(l=>l.level==='error'||l.level==='exception').map(l=>l.text.split('\n')[0].slice(0,160)),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
