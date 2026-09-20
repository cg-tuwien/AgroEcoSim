// Does an imported building cast a shadow across its WHOLE width?
//
// Reported as: with the facade loaded and daylight + shadows on, only the outermost fifth of the
// building is shadowed and the rest is flat-lit.
//
// Two defects produced that, and this harness measures both at once because neither is visible
// alone:
//
//   1. the GI density grid was fitted to the simulation FIELD - a 10.4 m cube - so ~73 % of a
//      37.7 m facade lay outside it. Geometry outside deposits nothing, and a surface point
//      outside leaves the sun march on its first probe and comes back fully lit.
//   2. traceSun ran 20 iterations, reaching ~31 cells (2.55 m), so even inside the grid nothing
//      could shadow anything further away than that.
//
// The measurement is deliberately BANDED across the model's width rather than a single
// whole-image number: a whole-image count cannot tell "a fifth of the building is shadowed" from
// "all of it is, faintly", and those call for opposite fixes.
//
// Needs the WebGPU backend (?gpu=1) and ?debughook=1 for window.__renderer / window.__appstate.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';
import { copyFileSync, existsSync, statSync } from 'node:fs';

const [url, objPath] = process.argv.slice(2);
const BANDS = +(process.env.BANDS || 5);
const W = +(process.env.W || 900), H = +(process.env.H || 400);

// Same trap obstaclepaint.mjs records: `npm run build` recreates the build directory, so the model
// has to be copied in every run - and sirv maps the directory at startup, so a file copied after
// it booted is served as a 404 page that is a perfectly valid File as far as the upload cares.
const SERVED = '/home/ubuntu/GreenFDT/ThreeFrontend/build/facade.obj';
const SOURCE = objPath
  || '/home/ubuntu/GreenFDT/Nooshin_Export/2026-September/fav11_complete_geometry_model_repaired_all_detected.obj';
if (!existsSync(SOURCE)) throw new Error(`no model at ${SOURCE}`);
if (!existsSync(SERVED) || statSync(SERVED).size !== statSync(SOURCE).size) {
  copyFileSync(SOURCE, SERVED);
  console.error(`Copied ${SOURCE} into the build directory. The static server must be RESTARTED `
    + `before it will serve it - sirv reads the directory once, at startup.`);
}

const browser = await launch(+(process.env.CDP_PORT || 25400));
let page;

const ev = async (p, e, awaitPromise = false) => {
  const r = await p.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise, timeout: 900000 });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  return r.result.value;
};

// renderToPixels, not a screenshot: the headless canvas never composites, so a screenshot of a
// WebGPU viewport is blank whatever the renderer did.
const shot = async (p) => await ev(p, `(async () => {
    const px = await window.__renderer.renderToPixels(${W}, ${H}, 1, 1, window.__renderer.getCameraPose());
    let s = '', v = new Uint8Array(px.data.buffer || px.data);
    const CH = 0x8000;
    for (let i = 0; i < v.length; i += CH) s += String.fromCharCode.apply(null, v.subarray(i, i + CH));
    return btoa(s); })()`, true);

/**
 * Per-band count of BUILDING pixels the shadow darkened, and by how much.
 *
 * Banded across the building's own pixel extent, not the image's. The facade fills about half
 * the frame at a pose that shows all of it, so banding the image put pure sky in the outer
 * bands - which reads as "the shadow does not reach the edges" and is the exact conclusion this
 * harness exists to test. Content is anything that differs from the corner pixel, which is
 * background by construction at this pose.
 */
const bandDiff = (litB64, shadowedB64) => {
  const lit = Buffer.from(litB64, 'base64'), dark = Buffer.from(shadowedB64, 'base64');
  const bg = [lit[0], lit[1], lit[2]];
  const isContent = i => Math.max(Math.abs(lit[i] - bg[0]), Math.abs(lit[i + 1] - bg[1]),
                                  Math.abs(lit[i + 2] - bg[2])) > 6;

  let minX = W, maxX = -1;
  for (let y = 0; y < H; ++y) for (let x = 0; x < W; ++x) {
    if (!isContent((y * W + x) * 4)) continue;
    if (x < minX) minX = x;
    if (x > maxX) maxX = x;
  }
  if (maxX < minX) return { error: 'no building pixels in frame' };

  const span = maxX - minX + 1;
  const perBand = span / BANDS;
  const bands = Array.from({ length: BANDS }, () => ({ darkened: 0, sum: 0, content: 0 }));
  for (let y = 0; y < H; ++y) {
    for (let x = minX; x <= maxX; ++x) {
      const i = (y * W + x) * 4;
      if (!isContent(i)) continue;
      const b = bands[Math.min(BANDS - 1, Math.floor((x - minX) / perBand))];
      ++b.content;
      // Luminance is not needed: a shadow darkens every channel, so the max drop is enough.
      const d = Math.max(lit[i] - dark[i], lit[i + 1] - dark[i + 1], lit[i + 2] - dark[i + 2]);
      if (d > 2) { ++b.darkened; b.sum += d; }
    }
  }
  return {
    buildingPixelsX: [minX, maxX],
    bands: bands.map((b, i) => ({
      band: i,
      contentPixels: b.content,
      darkenedPct: b.content ? +(100 * b.darkened / b.content).toFixed(2) : 0,
      meanDelta: b.darkened ? +(b.sum / b.darkened).toFixed(1) : 0,
    })),
  };
};

try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable');
  await page.send('Page.enable'); await page.send('DOM.enable');
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(20000)]);
  for (let i = 0; i < 60; ++i) { if (await ev(page, `!!window.__renderer`)) break; await sleep(1000); }
  await sleep(2000);

  const fetched = await ev(page, `(async () => {
      const res = await fetch('/facade.obj');
      if (!res.ok) return { ok: false, status: res.status };
      const blob = await res.blob();
      const head = await blob.slice(0, 200).text();
      if (!/^\\s*[#vogfsl]/m.test(head) || /<html|<!DOCTYPE/i.test(head))
        return { ok: false, status: res.status, bytes: blob.size, head: head.slice(0, 80) };
      const file = new File([blob], 'facade.obj', { type: 'text/plain' });
      await window.__appstate.uploadFieldModel(file);
      return { ok: true, bytes: blob.size }; })()`, true);
  if (!fetched.ok) throw new Error(`the model was not served: ${JSON.stringify(fetched)}`);

  let imported = null;
  for (let i = 0; i < 600; ++i) {
    await sleep(2000);
    imported = await ev(page, `(() => { const a = window.__appstate;
        const obs = a.obstacles.value.filter(o => o.meshData);
        return { withMesh: obs.length,
                 vertices: obs.reduce((n, o) => n + (o.meshData ? o.meshData.positions.length / 3 : 0), 0) }; })()`);
    if (imported.withMesh > 0 && imported.vertices > 0) break;
  }
  if (!imported.vertices) throw new Error(`import produced no geometry: ${JSON.stringify(imported)}`);

  // Frame the facade from its own bounds rather than a hardcoded pose: the import may place it
  // anywhere, and a camera that misses it reports "no shadow" for the wrong reason.
  const box = await ev(page, `(() => {
      const obs = window.__appstate.obstacles.value.filter(o => o.meshData);
      let mn = [1e9,1e9,1e9], mx = [-1e9,-1e9,-1e9];
      for (const o of obs) { const p = o.meshData.positions;
        for (let i = 0; i < p.length; i += 3) for (let c = 0; c < 3; ++c) {
          if (p[i+c] < mn[c]) mn[c] = p[i+c]; if (p[i+c] > mx[c]) mx[c] = p[i+c]; } }
      return { mn, mx }; })()`);
  console.error(`facade world bounds: ${JSON.stringify(box)}`);

  const posed = await ev(page, `(() => {
      const r = window.__renderer, mn = ${JSON.stringify(box.mn)}, mx = ${JSON.stringify(box.mx)};
      const c = [0,1,2].map(i => (mn[i] + mx[i]) / 2);
      const width = mx[0] - mn[0];
      const pose = r.getCameraPose();
      //Back off along +Z by the model's own width, and up to its mid-height: face-on, whole
      //width in frame, so a band of the image is a band of the building.
      pose.position = { x: c[0], y: c[1] + 0.2 * width, z: mx[2] + 0.9 * width };
      pose.target = { x: c[0], y: c[1], z: c[2] };
      r.setCameraPose(pose);
      return 'ok'; })()`);
  if (posed !== 'ok') throw new Error('camera override failed: ' + posed);

  const set = async (name, on) => await ev(page, `(() => {
      const el = document.querySelector('[name=${name}]');
      if (!el) return 'missing';
      if (el.checked !== ${on}) { el.checked = ${on};
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.dispatchEvent(new Event('change', { bubbles: true })); }
      return 'ok'; })()`);

  // Daylight on, so the sun carries the energy and a shadow has something to remove. With the
  // legacy flat ambient a shadow moves almost nothing.
  await set('lightDaylight', true);
  await sleep(1500);

  // The sun is driven directly rather than through the checkbox.
  //
  // resolveLighting takes the sun from the simulated clock, and with no run loaded the playhead
  // sits at timestep 0 - local midnight of the start date. The sun is below the horizon there,
  // so `shadows` resolves to FALSE however the box is ticked, and the first version of this
  // harness reported "0/5 bands shadowed" on a build where the shadow works perfectly. What is
  // under test is the density field and traceSun, not resolveLighting, which has unit tests.
  const setSun = async (on) => await ev(page, `(() => {
      const r = window.__renderer;
      const base = r.lighting;
      //#fff4e0 in linear, times DAY_SUN_INTENSITY.
      const sun = ${on} ? { r: 4.0, g: 3.63, b: 2.98 } : { r: 0, g: 0, b: 0 };
      const d = { x: 0.35, y: 0.55, z: 0.76 };
      const n = Math.hypot(d.x, d.y, d.z);
      r.setLighting({ ...base, shadows: ${on},
                      sunDirection: { x: d.x / n, y: d.y / n, z: d.z / n },
                      sunColor: sun });
      return 'ok'; })()`);

  await setSun(true);
  await sleep(2500);
  //Lit reference: the same sun, but with the occlusion trace off. Anything that differs between
  //the two is the shadow and nothing else - a comparison against an unlit scene would also
  //count the sun itself.
  await ev(page, `(() => { const r = window.__renderer;
      r.setLighting({ ...r.lighting, shadows: false }); return 'ok'; })()`);
  await sleep(2500);
  const lit = await shot(page);

  await setSun(true);
  await sleep(3500);
  const shadowed = await shot(page);

  const grid = await ev(page, `(() => { const g = window.__renderer.voxels && window.__renderer.voxels.grid;
      return g ? { dim: [g.dim.x, g.dim.y, g.dim.z], cell: +g.cell.x.toFixed(4),
                   origin: [g.origin.x, g.origin.y, g.origin.z].map(v => +v.toFixed(2)),
                   size: [g.size.x, g.size.y, g.size.z].map(v => +v.toFixed(2)) } : null; })()`);

  // Does switching an obstacle DATA MAPPING on still leave the building in the field?
  //
  // It used to not: the painted branch of appendObstacles carried IFlags.ExcludeFromGI, so
  // ticking a mapping removed the facade from the GI set entirely - the shadow vanished and the
  // fitted domain shrank back to the field box with it. The grid's own size is the probe,
  // because giStaticDigest skips exactly what the inject kernel skips: if the facade were
  // excluded it could no longer be setting the extent.
  const mapped = process.env.MAPPING === '0' ? null : await ev(page, `(() => {
      const a = window.__appstate;
      const obs = a.obstacles.value.filter(o => o.meshData);
      const names = [];
      for (const o of obs) for (const g of (o.groups || [])) names.push(g.name);
      if (!names.length) return { skipped: 'no named groups' };
      a.obstacleLight.value = names.map((n, i) => ({
          name: n, area: 1, exposedArea: 1, dominantOrientation: 'S', azimuthDegrees: 180,
          tiltDegrees: 90, centroid: [0,0,0], boundsMin: [0,0,0], boundsMax: [1,1,1],
          days: [], surfaces: [],
          summary: { days: 1, leafAreaIndex: (i % 2) ? 4 : 0, leafAreaIndexMax: (i % 2) ? 4 : 0,
                     leafAreaIndexFinal: (i % 2) ? 4 : 0, projectedLeafAreaIndex: (i % 2) ? 4 : 0,
                     leafAreaIndexFromGaps: (i % 2) ? 4 : 0, shadedFraction: (i % 2) ? 1 : 0,
                     avoidedTotal_kWh: i % 50, avoided: i % 50, beamTransmittance: 1 - (i % 100) / 100,
                     diffuseTransmittance: 1 - (i % 80) / 100, gapFractionDiffuse: (i % 90) / 100,
                     daylightHours: 10, directBeamHours: 5, beamOpenSky: 100, beam: 90,
                     diffuseOpenSky: 200, diffuse: 180, total: 270, totalOpenSky: 300,
                     peakOpenSky: 800, peakReduction: 100 } }));
      a.obstacleLightMeta.value = { initialDate: '2026-02-01', hoursPerTick: 4, dailySeriesOmitted: true,
                                    renderer: 'Internal', obstaclesMatched: names.length,
                                    obstaclesAvailable: names.length };
      a.obstacleMapping.value = 'leaf area index';
      return { names: names.length, mapping: a.obstacleMapping.value }; })()`);
  if (mapped && !mapped.skipped) await sleep(6000);
  const gridWithMapping = mapped && !mapped.skipped ? await ev(page, `(() => {
      const g = window.__renderer.voxels && window.__renderer.voxels.grid;
      return g ? { dim: [g.dim.x, g.dim.y, g.dim.z], size: [g.size.x, g.size.y, g.size.z].map(v => +v.toFixed(2)) } : null;
      })()`) : null;

  const measured = bandDiff(lit, shadowed);
  const bands = measured.bands || [];
  const covered = bands.filter(b => b.darkenedPct > 1).length;
  console.log(JSON.stringify({
    imported, grid, buildingPixelsX: measured.buildingPixelsX, bands,
    bandsWithShadow: `${covered}/${BANDS}`,
    mapped, gridWithMapping,
    mappingKeepsShadow: gridWithMapping
      ? (gridWithMapping.size[0] === (grid && grid.size[0]) ? 'yes - domain unchanged' : 'NO - domain shrank')
      : 'not tested',
    //The headline: before the fix only the band overlapping the field box darkened.
    verdict: covered === BANDS ? 'shadow spans the full width' : 'shadow is truncated',
    errors: logs.errors ? logs.errors.slice(0, 8) : undefined,
  }, null, 1));
} finally {
  if (page) await shutdown(browser, page);
  else await shutdown(browser);
}
