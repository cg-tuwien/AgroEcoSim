// Do the obstacle mapping colours hold still?
//
// Reported as flicker while orbiting, with vertices randomly grey and blue, and only when a mapping
// is on - without it every obstacle is a stable white. Two candidate causes, and they call for
// opposite fixes, so the harness separates them:
//
//   1. the REBUILD corrupts the colours. Picking runs on every mouse move and flips an obstacle's
//      hover state, which re-runs the obstacle rebuild - so "while the camera moves" may have
//      nothing to do with the camera. Rendering the same pose before and after a burst of mouse
//      moves isolates that.
//   2. the DRAW is view-dependent - two coplanar surfaces z-fighting, say, which is invisible while
//      everything is one colour and speckles the moment two colours meet.
//
// Needs an imported facade: procedural walls carry no colour array and are never painted.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';
import { writeFileSync, copyFileSync, existsSync, statSync } from 'node:fs';

const [url, objPath, outDir] = process.argv.slice(2);

// The model has to be reachable over HTTP, and `npm run build` recreates the build directory -
// which silently deleted it three times, so the harness uploaded a 404 page as if it were an OBJ
// and reported "import produced no colour groups". Copy it in here, every run, and check it.
const SERVED = '/home/ubuntu/GreenFDT/ThreeFrontend/build/facade.obj';
const SOURCE = objPath || '/home/ubuntu/GreenFDT/Nooshin_Export/2026-August/fav11_complete_geometry_model.obj';
if (!existsSync(SOURCE)) throw new Error(`no model at ${SOURCE}`);
if (!existsSync(SERVED) || statSync(SERVED).size !== statSync(SOURCE).size) {
  copyFileSync(SOURCE, SERVED);
  //sirv builds its file map when it STARTS, so a file copied afterwards is served as 404 no
  //matter that it is on disk - the same trap AGENTS.md records for a stale second server. Copying
  //here is therefore not enough on its own; say so rather than letting the next run fail again.
  console.error(`Copied ${SOURCE} into the build directory. The static server must be RESTARTED `
    + `before it will serve it - sirv reads the directory once, at startup.`);
}
const browser = await launch(+(process.env.CDP_PORT || 25300));
let page;

const ev = async (p, e, awaitPromise = false) => {
  const r = await p.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise, timeout: 900000 });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  return r.result.value;
};

//renderToPixels rather than a screenshot: the headless canvas never composites, so a screenshot of
//a WebGPU viewport is blank whatever the renderer did.
const shot = async (p, w, h) => await ev(p, `(async () => {
    const px = await window.__renderer.renderToPixels(${w}, ${h}, 1, 1, window.__renderer.getCameraPose());
    let s = '', v = new Uint8Array(px.data.buffer || px.data);
    const CH = 0x8000;
    for (let i = 0; i < v.length; i += CH) s += String.fromCharCode.apply(null, v.subarray(i, i + CH));
    return btoa(s); })()`, true);

const diff = (a, b) => {
  const A = Buffer.from(a, 'base64'), B = Buffer.from(b, 'base64');
  let n = 0, worst = 0;
  for (let i = 0; i < Math.min(A.length, B.length); i += 4) {
    const d = Math.max(Math.abs(A[i] - B[i]), Math.abs(A[i + 1] - B[i + 1]), Math.abs(A[i + 2] - B[i + 2]));
    if (d > 0) { ++n; worst = Math.max(worst, d); }
  }
  return { differing: n, worst, pixels: Math.min(A.length, B.length) / 4 };
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

  // Import the facade through the app's own upload path. The control is a drag-and-drop zone with
  // no file input behind it, so the file is fetched (it is served beside the bundle) and handed to
  // the same method the drop handler calls - the app's own code, not a shortcut around it.
  //Checked before it is used. An HTML error page is a perfectly valid File as far as the upload
  //is concerned; it just parses to nothing, and the failure surfaces much later as an empty
  //obstacle list that looks like a bug in the thing under test.
  const fetched = await ev(page, `(async () => {
      const res = await fetch('/facade.obj');
      if (!res.ok) return { ok: false, status: res.status };
      const blob = await res.blob();
      const head = await blob.slice(0, 200).text();
      //An OBJ starts with comments, vertices or object names - never with a tag.
      if (!/^\s*[#vogfsl]/m.test(head) || /<html|<!DOCTYPE/i.test(head))
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
        return { obstacles: a.obstacles.value.length, withMesh: obs.length,
                 groups: obs.reduce((n, o) => n + (o.groups ? o.groups.length : 0), 0),
                 vertices: obs.reduce((n, o) => n + (o.meshData ? o.meshData.positions.length / 3 : 0), 0) }; })()`);
    if (imported.withMesh > 0 && imported.groups > 0) break;
  }
  if (!imported.groups) throw new Error(`import produced no colour groups: ${JSON.stringify(imported)}`);

  // Fabricate a measurement over the imported names. This is a RENDERING test: the numbers only
  // have to reach the painter, and running a real season first would take hours and prove nothing
  // extra about the shader.
  const mapped = await ev(page, `(() => { const a = window.__appstate;
      const obs = a.obstacles.value.filter(o => o.meshData);
      const names = [];
      for (const o of obs) for (const g of (o.groups || [])) names.push(g.name);
      a.obstacleLight.value = names.map((n, i) => ({
          name: n, area: 1, exposedArea: 1, dominantOrientation: 'S', azimuthDegrees: 180, tiltDegrees: 90,
          centroid: [0,0,0], boundsMin: [0,0,0], boundsMax: [1,1,1], days: [], surfaces: [],
          //Every field the HUD table renders. A partial summary throws inside a component's
          //render on toFixed of undefined, which then looks like a rendering bug in the thing
          //under test - the harness's own noise, mistaken for a finding.
          //(No backticks in here: this whole string is a template literal and one would end it.)
          //ALTERNATING EXTREMES, not a smooth ramp. A smooth ramp gives neighbouring trays nearly
          //the same colour, so a depth tie between them resolves to almost the same pixel either
          //way and the fault is invisible - which is exactly why the first version of this harness
          //reported "stable" on a scene that flickers violently in front of a person.
          summary: { days: 1, leafAreaIndex: (i % 2) ? 4 : 0, leafAreaIndexMax: (i % 2) ? 4 : 0,
                     leafAreaIndexFinal: (i % 2) ? 4 : 0, projectedLeafAreaIndex: (i % 2) ? 4 : 0,
                     leafAreaIndexFromGaps: (i % 2) ? 4 : 0, shadedFraction: (i % 2) ? 1 : 0,
                     avoidedTotal_kWh: i % 50, avoided: i % 50, beamTransmittance: 1 - (i % 100) / 100,
                     diffuseTransmittance: 1 - (i % 80) / 100, gapFractionDiffuse: (i % 90) / 100,
                     daylightHours: 10, directBeamHours: 5, beamOpenSky: 100, beam: 90,
                     diffuseOpenSky: 200, diffuse: 180, total: 270, totalOpenSky: 300,
                     peakOpenSky: 800, peakReduction: 100 } }));
      a.obstacleLightMeta.value = { initialDate: '2026-02-01', hoursPerTick: 4, dailySeriesOmitted: true,
                                    renderer: 'Internal', obstaclesMatched: names.length, obstaclesAvailable: names.length };
      a.obstacleMapping.value = 'leaf area index';
      return { names: names.length, mapping: a.obstacleMapping.value,
               values: a.obstacleMappingValues.value ? a.obstacleMappingValues.value.size : 0 }; })()`);
  await sleep(3000);

  // Frame the model, or the whole test renders empty background and reports a confident zero.
  // The first version of this harness did exactly that: "0 differing" on two blank images.
  const framed = await ev(page, `(() => {
      const o = window.__appstate.obstacles.value.find(x => x.meshData);
      const p = o.meshData.positions;
      let lo = [Infinity, Infinity, Infinity], hi = [-Infinity, -Infinity, -Infinity];
      for (let i = 0; i < p.length; i += 3)
        for (let k = 0; k < 3; ++k) { if (p[i + k] < lo[k]) lo[k] = p[i + k]; if (p[i + k] > hi[k]) hi[k] = p[i + k]; }
      const c = [(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2];
      const r = Math.max(hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]) || 1;
      const pose = window.__renderer.getCameraPose();
      pose.target = { x: c[0], y: c[1], z: c[2] };
      //Close enough that the surfaces FILL the frame. Framing the whole model leaves it a sliver
      //of a few thousand pixels, and a speckle artifact a few pixels wide would simply be missed -
      //a negative result from a picture that barely contains the subject is not a negative result.
      const d = r * ${process.env.FILL || 0.06};
      pose.position = { x: c[0] + d, y: c[1] + d * 0.6, z: c[2] + d };
      //NOT touched. Overriding these silently substituted the harness's own depth range for the
      //app's, so a change to CAMERA_NEAR/CAMERA_FAR produced a byte-identical result and looked
      //like "the fix does nothing" - when the fix had simply never been under test.
      //(The override was near 0.0377 / far 754, a ratio of 20,000:1, which flickers just as badly:
      //that is itself the evidence that narrowing the range is not the answer here.)
      window.__renderer.setCameraPose(pose);
      return { min: lo, max: hi, radius: r, near: pose.near, far: pose.far }; })()`);
  await sleep(2000);

  const W = 640, H = 400;
  const a1 = await shot(page, W, H);

  // A blank render makes every later comparison meaningless, so refuse to continue on one.
  const coverage = (b64) => {
    const A = Buffer.from(b64, 'base64');
    const bg = [A[0], A[1], A[2]];
    let n = 0;
    for (let i = 0; i < A.length; i += 4)
      if (A[i] !== bg[0] || A[i + 1] !== bg[1] || A[i + 2] !== bg[2]) ++n;
    return n;
  };
  //Every group was given a value, so NOTHING should render as the unmeasured background grey.
  //Any pixel of it on the facade is evidence of something wrong: a second unpainted surface
  //fighting for the same depth, or colours written where they do not belong. This is a sharper
  //test than comparing two renders, because it does not need the fault to be unstable.
  const backgroundGrey = (b64) => {
    const A = Buffer.from(b64, 'base64');
    const bg = [A[0], A[1], A[2]];
    let grey = 0, ramp = 0;
    for (let i = 0; i < A.length; i += 4) {
      if (A[i] === bg[0] && A[i + 1] === bg[1] && A[i + 2] === bg[2]) continue;
      //unmeasuredObstacleColor is 0.02 linear, about 38 in sRGB, and near-neutral. The ramp
      //never goes that dark.
      const [r0, g0, b0] = [A[i], A[i + 1], A[i + 2]];
      const neutral = Math.abs(r0 - g0) < 8 && Math.abs(g0 - b0) < 10;
      if (neutral && r0 < 70) ++grey; else ++ramp;
    }
    return { grey, ramp };
  };

  const drawn = coverage(a1);
  if (drawn < 1000) throw new Error(`the render is empty (${drawn} non-background pixels) - nothing was framed, so any comparison would be meaningless`);

  // (1) Same pose, nothing touched. Any difference here is the draw itself being unstable.
  const a2 = await shot(page, W, H);
  const idle = diff(a1, a2);

  // (2) Same pose, but with a burst of mouse moves in between - which is what orbiting actually
  // does to the obstacle rebuild, via picking and the hover state.
  const before = await ev(page, `window.__rendererStats ? window.__rendererStats() : null`);
  for (let i = 0; i < 24; ++i) {
    await page.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: 300 + (i % 12) * 40, y: 200 + (i % 7) * 30 });
    await sleep(60);
  }
  await sleep(1500);
  const a3 = await shot(page, W, H);
  const afterMouse = diff(a1, a3);

  //And the reported case: a SMALL camera move. Two nearly identical views of a scene whose
  //surfaces are microns apart will disagree wildly if the depth buffer cannot separate them, and
  //that disagreement is the flicker - it is not visible from any single frame.
  const nudged = await ev(page, `(() => { const p = window.__renderer.getCameraPose();
      p.position = { x: p.position.x * 1.002, y: p.position.y, z: p.position.z };
      window.__renderer.setCameraPose(p); return 1; })()`);
  await sleep(800);
  const a4 = await shot(page, W, H);
  const afterNudge = diff(a3, a4);

  if (outDir) {
    writeFileSync(`${outDir}/paint-1.raw`, Buffer.from(a1, 'base64'));
    writeFileSync(`${outDir}/paint-3.raw`, Buffer.from(a3, 'base64'));
  }

  //The counters matter as much as the pixels. Orbiting must not repaint or re-upload anything -
  //nothing about a camera move changes a measured value - and a full atlas re-upload per
  //mouse-move event is the documented cause of this exact flicker.
  const repaints = await ev(page, `(() => { const a = window.__appstate;
      return { mappingValues: a.obstacleMappingValues.value ? a.obstacleMappingValues.value.size : 0,
               renderer: window.__rendererStats ? window.__rendererStats() : null }; })()`);

  console.log(JSON.stringify({
    servedModelBytes: fetched.bytes,
    imported, mapped, repaints, framed, nonBackgroundPixels: drawn,
    //grey must be 0: every group has a value in this test.
    facadePixels: backgroundGrey(a1),
    samePoseTwice: idle,
    samePoseAfter24MouseMoves: afterMouse,
    statsBeforeMouseMoves: before,
    afterTinyCameraMove: afterNudge,
    verdict: backgroundGrey(a1).grey > 0
        ? 'UNMEASURED GREY ON A FULLY MEASURED FACADE - a second surface or a stray write'
      : idle.differing > 0 ? 'THE DRAW ITSELF IS UNSTABLE at a fixed pose'
      : afterMouse.differing > 0 ? 'THE REBUILD CORRUPTS THE COLOURS - mouse moves change the picture at a fixed pose'
      : before && repaints.renderer && repaints.renderer.atlasUploads > before.atlasUploads
        ? 'STABLE PIXELS, BUT THE ATLAS RE-UPLOADED WHILE ONLY THE MOUSE MOVED'
      : 'STABLE - neither repeated draws nor mouse-driven rebuilds change the picture',
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0, 200)).filter(t => !/favicon/.test(t)).slice(0, 5),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
