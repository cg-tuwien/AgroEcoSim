// Does the WebGPU renderer mount at all?
//
// The narrowest possible check, written because "the context crashes immediately" needs a first
// answer that is not a guess: did the device come up, did a pipeline compile, did the first frame
// draw anything, and what exactly did the console say. Nothing is loaded, nothing is simulated.
//
// Reports the FULL first error rather than a truncated line: a WebGPU validation failure names the
// binding or the shader that broke, and that name is the whole diagnosis.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';

const [url] = process.argv.slice(2);
const browser = await launch(+(process.env.CDP_PORT || 25400));
let page;

const ev = async (p, e, awaitPromise = false) => {
  const r = await p.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise, timeout: 300000 });
  if (r.exceptionDetails) return { __threw: r.exceptionDetails.exception?.description || r.exceptionDetails.text };
  return r.result.value;
};

try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable'); await page.send('Page.enable');
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(20000)]);

  let mounted = null;
  for (let i = 0; i < 40; ++i) {
    await sleep(1000);
    mounted = await ev(page, `(() => ({
        hasRenderer: !!window.__renderer,
        kind: window.__renderer ? window.__renderer.kind : null,
        backend: window.__appstate ? window.__appstate.activeBackend.value : null }))()`);
    if (mounted && mounted.kind) break;
  }

  //Did it draw? A mounted renderer that produces a flat clear is a failure wearing a success.
  let drew = null;
  if (mounted?.kind) {
    const px = await ev(page, `(async () => {
        const p = await window.__renderer.renderToPixels(160, 100, 1, 1, window.__renderer.getCameraPose());
        const v = new Uint8Array(p.data.buffer || p.data);
        let distinct = new Set();
        for (let i = 0; i < v.length; i += 4) distinct.add((v[i] << 16) | (v[i+1] << 8) | v[i+2]);
        return { bytes: v.length, distinctColours: distinct.size }; })()`, true);
    drew = px;
  }

  const errors = logs.filter(l => l.level === 'error' || l.level === 'exception')
    .map(l => l.text).filter(t => !/favicon|CONNECTION_REFUSED|Failed to fetch/.test(t));
  const warnings = logs.filter(l => l.level === 'warning')
    .map(l => l.text).filter(t => /gpu|webgpu|device|pipeline|shader|adapter|buffer|binding/i.test(t));

  console.log(JSON.stringify({
    requested: url,
    mounted,
    firstFrame: drew,
    verdict: !mounted?.kind ? 'NOTHING MOUNTED'
      : mounted.kind !== 'webgpu' ? `FELL BACK TO ${String(mounted.kind).toUpperCase()}`
      : drew?.distinctColours > 1 ? 'WEBGPU MOUNTED AND DREW'
      : 'WEBGPU MOUNTED BUT DREW NOTHING',
    //Whole messages. A truncated WebGPU validation error loses the part that names the cause.
    errors: errors.slice(0, 4).map(t => t.slice(0, 1200)),
    gpuWarnings: warnings.slice(0, 6).map(t => t.slice(0, 600)),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
