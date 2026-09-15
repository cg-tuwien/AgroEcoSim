// Is the same surface being drawn twice?
//
// The reported flicker is diagonal hatching and horizontal scanlines over the trays that shift with
// the camera, in the WebGL frontend. That is the signature of two coplanar surfaces fighting for
// the depth buffer, not of wrong vertex colours - and it only shows once a mapping gives the two
// different colours, which is exactly why it was invisible while everything was white.
//
// So the question is geometric, not graphical, and it can be answered without rendering anything:
// does the imported scene contain the tray geometry TWICE - once as terrain and once as an
// obstacle? Hardware, backend and camera are all irrelevant to that.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';

const [url] = process.argv.slice(2);
const browser = await launch(+(process.env.CDP_PORT || 25500));
let page;

const ev = async (p, e, awaitPromise = false) => {
  const r = await p.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise, timeout: 900000 });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  return r.result.value;
};

try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable'); await page.send('Page.enable');
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(20000)]);
  for (let i = 0; i < 60; ++i) { if (await ev(page, `!!window.__appstate`)) break; await sleep(1000); }
  await sleep(2000);

  await ev(page, `(async () => {
      const res = await fetch('/facade.obj');
      const file = new File([await res.blob()], 'facade.obj', { type: 'text/plain' });
      await window.__appstate.uploadFieldModel(file);
      return 1; })()`, true);

  let ready = null;
  for (let i = 0; i < 600; ++i) {
    await sleep(2000);
    ready = await ev(page, `(() => { const a = window.__appstate;
        const obs = a.obstacles.value.filter(o => o.meshData);
        return { obstacles: obs.length, terrainItems: a.terrainList ? a.terrainList.length : 0 }; })()`);
    if (ready.obstacles > 0 && ready.terrainItems > 0) break;
  }

  // Hash every terrain vertex, then ask how many obstacle vertices land on one. Quantised to a
  // tenth of a millimetre: geometry that came from the same source file matches exactly, and the
  // tolerance only guards against float round-trips through the wire.
  const overlap = await ev(page, `(() => {
      const a = window.__appstate;
      const key = (x, y, z) => (Math.round(x * 1e4) + ',' + Math.round(y * 1e4) + ',' + Math.round(z * 1e4));

      const terrain = new Set();
      let terrainVerts = 0;
      for (const t of (a.terrainList || [])) {
        const p = t.meshData && t.meshData.positions;
        if (!p) continue;
        terrainVerts += p.length / 3;
        for (let i = 0; i < p.length; i += 3) terrain.add(key(p[i], p[i+1], p[i+2]));
      }

      let obstacleVerts = 0, shared = 0;
      const boxes = [];
      for (const o of a.obstacles.value) {
        const p = o.meshData && o.meshData.positions;
        if (!p) continue;
        obstacleVerts += p.length / 3;
        let lo = [Infinity,Infinity,Infinity], hi = [-Infinity,-Infinity,-Infinity];
        for (let i = 0; i < p.length; i += 3) {
          if (terrain.has(key(p[i], p[i+1], p[i+2]))) ++shared;
          for (let k = 0; k < 3; ++k) { if (p[i+k] < lo[k]) lo[k] = p[i+k]; if (p[i+k] > hi[k]) hi[k] = p[i+k]; }
        }
        boxes.push({ lo, hi });
      }
      return { terrainItems: (a.terrainList || []).length, terrainVerts, obstacleVerts, shared,
               sharedPercent: obstacleVerts ? +(100 * shared / obstacleVerts).toFixed(1) : 0,
               obstacleBounds: boxes[0] }; })()`);

  // How close together are distinct surfaces in the facade itself?
  //
  // If neighbouring trays are back to back within a fraction of a millimetre, no colouring bug is
  // needed to explain the hatching: the depth buffer simply cannot tell the two panels apart, and
  // the moment a mapping gives them different colours the tie shows. This measures the geometry,
  // so it is independent of hardware, backend and camera.
  const gaps = await ev(page, `(() => {
      const o = window.__appstate.obstacles.value.find(x => x.meshData);
      const p = o.meshData.positions;
      //Z is the thin axis of this facade; distinct panel planes stack along it.
      const zs = new Set();
      for (let i = 2; i < p.length; i += 3) zs.add(Math.round(p[i] * 1e6) / 1e6);
      const sorted = [...zs].sort((a, b) => a - b);
      const diffs = [];
      for (let i = 1; i < sorted.length; ++i) diffs.push(sorted[i] - sorted[i - 1]);
      diffs.sort((a, b) => a - b);
      return { distinctZPlanes: sorted.length,
               zSpan: +(sorted[sorted.length - 1] - sorted[0]).toFixed(4),
               smallestGapsMm: diffs.slice(0, 8).map(d => +(d * 1000).toFixed(4)),
               medianGapMm: +(diffs[Math.floor(diffs.length / 2)] * 1000).toFixed(4) }; })()`);

  // What the depth buffer can actually resolve there, for a standard (non-reversed) projection.
  const depthResolutionMm = (z, near, far, bits) =>
    +((z * z / (near * (1 - near / far))) / Math.pow(2, bits) * 1000).toFixed(4);

  console.log(JSON.stringify({
    ready,
    overlap,
    facadeGeometry: gaps,
    depthBufferResolutionMm: {
      'at 10 m, near 0.025, far 5000, 24-bit': depthResolutionMm(10, 0.025, 5000, 24),
      'at 20 m, near 0.025, far 5000, 24-bit': depthResolutionMm(20, 0.025, 5000, 24),
      'at 20 m, near 0.1,   far 200,  24-bit': depthResolutionMm(20, 0.1, 200, 24),
    },
    //Any sizeable share means the same triangles exist in both lists, drawn twice at the same
    //depth - which no polygon offset can separate, because the offset applies to both equally.
    verdict: overlap.sharedPercent > 5
      ? 'THE SAME GEOMETRY IS IN BOTH THE TERRAIN AND THE OBSTACLES - drawn twice, coplanar'
      : 'terrain and obstacle geometry are distinct',
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0, 200)).filter(t => !/favicon/.test(t)).slice(0, 4),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
