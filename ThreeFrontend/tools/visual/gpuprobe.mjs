// Reports what WebGPU, if anything, this browser can provide.
// Kept because "can the WebGPU backend be verified here at all?" is a question worth being
// able to answer in one command.
import { launch, newPage, sleep, shutdown } from './cdp.mjs';

const browser = await launch(+(process.env.CDP_PORT || 9960));
let page;
try {
  page = await newPage(browser.port);
  await page.ready;
  await page.send('Runtime.enable'); await page.send('Page.enable');
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url: process.argv[2] || 'http://127.0.0.1:8099/' });
  await Promise.race([loaded, sleep(12000)]);
  await sleep(1500);
  const r = await page.send('Runtime.evaluate', {
    awaitPromise: true, returnByValue: true,
    expression: `(async () => {
      if (!navigator.gpu) return { gpu: false };
      try {
        const a = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' });
        if (!a) {
          const fb = await navigator.gpu.requestAdapter({ forceFallbackAdapter: true });
          return { gpu: true, adapter: false, fallbackAdapter: !!fb };
        }
        const d = await a.requestDevice();
        return { gpu: true, adapter: true, device: !!d,
                 maxTextureDimension2D: d.limits.maxTextureDimension2D,
                 maxStorageBuffersPerShaderStage: d.limits.maxStorageBuffersPerShaderStage,
                 format: navigator.gpu.getPreferredCanvasFormat() };
      } catch (e) { return { gpu: true, error: String(e) }; }
    })()`,
  });
  console.log(JSON.stringify(r.result.value));
} finally { page?.close(); await shutdown(browser); }
