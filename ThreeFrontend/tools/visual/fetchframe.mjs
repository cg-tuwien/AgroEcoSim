// Proves the browser inflates a Content-Encoding: deflate frame by itself, so the client needs
// no JS decompression on the streaming path at all.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';

const [url, runId, backend] = process.argv.slice(2);
const browser = await launch(+(process.env.CDP_PORT || 23800));
let page;
try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable'); await page.send('Page.enable');
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(15000)]);
  await sleep(2500);

  const r = await page.send('Runtime.evaluate', { returnByValue: true, awaitPromise: true, expression: `(async () => {
      const base = '${backend}/Simulation/run/${runId}';
      const idxRes = await fetch(base + '/index');
      const idx = new Uint32Array(await idxRes.arrayBuffer());
      const frames = idx.length / 2;

      const last = frames - 1;
      const res = await fetch(base + '/frame/' + last);
      const bytes = new Uint8Array(await res.arrayBuffer());

      // Decode it with the app's own reader to prove it is a usable scene, not just bytes.
      const scene = window.__decodeSceneBytes ? window.__decodeSceneBytes(bytes) : null;

      return {
        frames,
        storedBytesFromIndex: idx[last * 2 + 1],
        receivedBytes: bytes.length,
        // If the browser inflated it, what we received is LARGER than what is stored.
        browserInflated: bytes.length > idx[last * 2 + 1],
        version: bytes[0], flags: bytes[1],
        entities: new DataView(bytes.buffer).getUint32(2, true),
        decodedPlants: scene ? scene.length : null,
        decodedPrimitives: scene ? scene.reduce((n,p)=>n+p.primitives.length,0) : null,
      };
    })()` });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  console.log(JSON.stringify({ ...r.result.value,
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0,140)).slice(0,3) }, null, 1));
} finally { page?.close(); await shutdown(browser); }
