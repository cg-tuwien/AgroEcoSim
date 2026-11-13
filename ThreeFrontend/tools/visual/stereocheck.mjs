// Checks the stereo checkerboard end to end in a real browser, on the backend the URL selects.
//
//   node stereocheck.mjs "http://localhost:8080/?debughook=1&gpu=0" ./fixture-session.json
//   CHROME_EXTRA_FLAGS="--enable-unsafe-webgpu --enable-features=Vulkan --use-webgpu-adapter=swiftshader" \
//     node stereocheck.mjs "http://localhost:8080/?debughook=1&gpu=1" ./fixture-session.json
//
// The stereo settings are driven through the Rendering tab's own controls. What is captured is
// the LIVE canvas on WebGL (headless Chromium composites it) and, on WebGPU, the same passes read
// back through __renderer.readStereoPixels() (headless never composites a WebGPU canvas).
//
// 1. Test pattern: every canvas pixel is red where (x + y) is even, counting rows from the top,
//    and blue elsewhere; swapped with "swap eyes".
// 2. Zero eye separation: both eyes are the centre camera, so the interleave equals the mono view.
// 3. A real separation: the near edge of the soil sits further right for the left eye (crossed
//    disparity) than for the right one.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';

const [url, fixture] = process.argv.slice(2);
const browser = await launch(+(process.env.CDP_PORT || 22100));
let page;
let failures = 0;
const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'} ${what}`); if (!ok) failures++; };
try {
  page = await newPage(browser.port); await page.ready;
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

  // Load the session through the app's own button (a trusted click opens the chooser).
  const chooser = new Promise(r => page.on(m => { if (m.method === 'Page.fileChooserOpened') r(m.params); }));
  await page.send('Page.setInterceptFileChooserDialog', { enabled: true });
  const pt = await ev(`(() => { const b=[...document.querySelectorAll('button')].find(x=>['Load','Load run'].includes(x.textContent.trim()));
    const r=b.getBoundingClientRect(); return {x:Math.round(r.left+r.width/2),y:Math.round(r.top+r.height/2)}; })()`);
  for (const type of ['mousePressed', 'mouseReleased'])
    await page.send('Input.dispatchMouseEvent', { type, x: pt.x, y: pt.y, button: 'left', clickCount: 1 });
  const ch = await Promise.race([chooser, sleep(10000).then(() => null)]);
  if (!ch) throw new Error('no file chooser');
  await page.send('DOM.setFileInputFiles', { files: [fixture], backendNodeId: ch.backendNodeId });
  await sleep(7000);
  await ev(`(() => { const b=document.querySelector('button[title="Seek to end"]'); if(b) b.click(); return 1; })()`);
  await sleep(3000);
  const kind = await ev(`window.__renderer?.kind`);
  console.log(`backend: ${kind}`);
  await ev(`(() => { window.__appstate.activeTab.value = 'tab-rendering';
    window.__renderer.setCameraPose({ position:{x:0.6,y:0.5,z:1.0}, target:{x:0,y:0.15,z:0}, up:{x:0,y:1,z:0}, fovY:50, near:0.05, far:500 });
    return 1; })()`);
  await sleep(1500);

  const set = (name, value) => ev(`(() => { const el=document.querySelector('[name=${name}]'); if(!el) return 'missing';
    if (el.type === 'checkbox') el.checked = ${JSON.stringify(value)}; else el.value = ${JSON.stringify(String(value))};
    el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true}));
    return el.type === 'checkbox' ? el.checked : el.value; })()`);

  // Pixels of the 3D view, rows from the top, kept in the page under a name.
  const capture = async name => {
    await sleep(2500);
    if (kind === 'webgpu') {
      return ev(`(async () => { const r = await window.__renderer.readStereoPixels();
        if (!r) { const c=document.querySelector('#main3Dviewport canvas');
          const m = await window.__renderer.renderToPixels(c.width, c.height, 1, 4, window.__renderer.getCameraPose());
          (window.__shots ??= {})[${JSON.stringify(name)}] = m; return [m.width, m.height, 'mono-offline']; }
        (window.__shots ??= {})[${JSON.stringify(name)}] = r; return [r.width, r.height, 'stereo-readback']; })()`, true);
    }
    // The HUD floats over the canvas; hide it for the shot so only the 3D view is measured.
    await ev(`(() => { document.getElementById('hud').style.visibility = 'hidden'; return 1; })()`);
    await sleep(300);
    const { data } = await page.send('Page.captureScreenshot', { format: 'png', fromSurface: true });
    await ev(`(() => { document.getElementById('hud').style.visibility = ''; return 1; })()`);
    return ev(`(async () => {
      const c = document.querySelector('#main3Dviewport canvas'); const rect = c.getBoundingClientRect();
      const bmp = await createImageBitmap(await (await fetch('data:image/png;base64,${data}')).blob());
      const oc = new OffscreenCanvas(bmp.width, bmp.height); const g = oc.getContext('2d'); g.drawImage(bmp, 0, 0);
      const img = g.getImageData(Math.round(rect.left), Math.round(rect.top), c.width, c.height);
      (window.__shots ??= {})[${JSON.stringify(name)}] = { data: img.data, width: img.width, height: img.height };
      return [img.width, img.height, 'canvas-screenshot', rect.left, rect.top]; })()`, true);
  };

  const patternMismatch = (name, swap) => ev(`(() => { const s = window.__shots[${JSON.stringify(name)}]; let bad = 0;
    for (let y = 0; y < s.height; ++y) for (let x = 0; x < s.width; ++x) {
      const i = (y * s.width + x) * 4; const left = (((x + y) & 1) === 0) !== ${swap};
      const r = s.data[i], b = s.data[i + 2];
      if (left ? !(r > 250 && b < 5) : !(b > 250 && r < 5)) bad++; }
    return [bad, s.width * s.height]; })()`);

  const differ = (a, b) => ev(`(() => { const p = window.__shots[${JSON.stringify(a)}], q = window.__shots[${JSON.stringify(b)}];
    let n = 0, max = 0; for (let i = 0; i < p.data.length; i += 4) { let d = 0;
      for (let k = 0; k < 3; ++k) d = Math.max(d, Math.abs(p.data[i+k] - q.data[i+k])); if (d) { n++; max = Math.max(max, d); } }
    return [n, max, p.width * p.height]; })()`);

  // 3. near soil edge per eye: first soil pixel from the left on a few rows near the bottom
  const soilEdges = name => ev(`(() => { const s = window.__shots[${JSON.stringify(name)}]; const out = [];
    for (const y of [s.height - 4, s.height - 40, s.height - 80]) { const first = { L: -1, R: -1 };
      for (let x = 0; x < s.width; ++x) { const i = (y * s.width + x) * 4; const r = s.data[i], g = s.data[i+1], b = s.data[i+2];
        const e = ((x + y) & 1) === 0 ? 'L' : 'R'; if (first[e] < 0 && r > 70 && r > g + 20 && g > b + 20 && b < 60) first[e] = x; }
      out.push([y, first.L, first.R]); } return out; })()`);

  console.log('mono', await capture('mono'));
  check(await set('stereoMode', 'checkerboard') === 'checkerboard', 'stereo mode switched through the control');
  await set('stereoTestPattern', true);
  console.log('pattern', await capture('pattern'));
  const [badP, totalP] = await patternMismatch('pattern', false);
  check(badP === 0, `test pattern: ${badP} of ${totalP} pixels on the wrong eye`);
  await set('stereoSwapEyes', true);
  await capture('swapped');
  const [badS] = await patternMismatch('swapped', true);
  check(badS === 0, `swapped test pattern: ${badS} pixels on the wrong eye`);
  await set('stereoSwapEyes', false);
  await set('stereoTestPattern', false);
  await set('stereoEyeSeparation', 0);
  await capture('zero');
  const [nZero, maxZero, total] = await differ('mono', 'zero');
  // Off the edges the eye images must equal mono. On them they may not: WebGL's eyes resolve
  // their multisamples in linear half float and encode once in the composite, where the canvas
  // resolves samples it has already encoded, so an anti-aliased edge blends differently.
  const [interior, interiorMax] = await ev(`(() => { const p = window.__shots.mono, q = window.__shots.zero, w = p.width, h = p.height;
    let n = 0, max = 0; const same = (i, j) => p.data[i] === p.data[j] && p.data[i+1] === p.data[j+1] && p.data[i+2] === p.data[j+2];
    for (let y = 1; y < h - 1; ++y) for (let x = 1; x < w - 1; ++x) { const i = (y * w + x) * 4; let flat = true;
      for (let dy = -1; dy <= 1 && flat; ++dy) for (let dx = -1; dx <= 1; ++dx) if (!same(i, ((y + dy) * w + x + dx) * 4)) { flat = false; break; }
      if (!flat) continue; let d = 0; for (let k = 0; k < 3; ++k) d = Math.max(d, Math.abs(p.data[i+k] - q.data[i+k]));
      if (d > 1) { n++; max = Math.max(max, d); } }
    return [n, max]; })()`);
  check(interior <= total * 0.0005 && nZero <= total * 0.02,
    `zero separation equals mono off the edges: ${interior} flat pixels differ by more than 1 (max ${interiorMax}); ${nZero} of ${total} differ in all (max ${maxZero})`);
  await set('stereoEyeSeparation', 0.06);
  await capture('stereo');
  const [nStereo] = await differ('mono', 'stereo');
  check(nStereo > nZero, `a 6 cm separation changes the image (${nStereo} pixels)`);
  const edges = await soilEdges('stereo');
  console.log('soil edge per row [y, left eye x, right eye x]:', JSON.stringify(edges));
  const crossed = edges.filter(([, l, r]) => l >= 0 && r >= 0);
  check(crossed.length > 0 && crossed.every(([, l, r]) => l > r), 'near soil has crossed disparity (left eye sees it further right)');
  // 4. An off-axis eye is a translated camera whose image is shifted sideways by ndcShift * W / 2
  //    pixels. Choose the separation so that shift is a whole 8 pixels and compare the left eye,
  //    with every lighting effect on, against a mono render from the left eye's position: the
  //    occlusion, the cone-traced shadows and the sky all rebuild rays through the shift, so any
  //    error there shows up as a difference here.
  if (process.env.SKIP_SHIFT !== '1') {
    for (const box of ['lightDaylight', 'lightShadows', 'lightOcclusion', 'lightIndirect', 'lightSky']) await set(box, true);
    await set('stereoConvergeOnTarget', false);
    await set('stereoConvergence', 2);
    const geo = await ev(`(() => { const c = document.querySelector('#main3Dviewport canvas'); return [c.width, c.height]; })()`);
    const [W, H] = geo, k = 8, conv = 2, tanY = Math.tan(50 * Math.PI / 360);
    const sep = 2 * conv * tanY * (W / H) * (k / (W / 2));
    await set('stereoEyeSeparation', sep);
    await capture('shifted');
    await ev(`(async () => {
      const p = window.__renderer.getCameraPose();
      const f = [p.target.x - p.position.x, p.target.y - p.position.y, p.target.z - p.position.z];
      const r = [f[1] * p.up.z - f[2] * p.up.y, f[2] * p.up.x - f[0] * p.up.z, f[0] * p.up.y - f[1] * p.up.x];
      const l = Math.hypot(...r); const o = -${sep} / 2;
      const move = v => ({ x: v.x + r[0] / l * o, y: v.y + r[1] / l * o, z: v.z + r[2] / l * o });
      window.__shots.leftEyeMono = await window.__renderer.renderToPixels(${W}, ${H}, 1, 4, { ...p, position: move(p.position), target: move(p.target) });
      return 1; })()`, true);
    const [cmp, flatBad, flatMax, allBad] = await ev(`(() => { const s = window.__shots.shifted, m = window.__shots.leftEyeMono, w = s.width, h = s.height;
      let cmp = 0, flatBad = 0, flatMax = 0, allBad = 0;
      const px = (img, x, y, k) => img.data[(y * w + x) * 4 + k];
      for (let y = 1; y < h - 1; ++y) for (let x = 16; x < w - 16; ++x) { if (((x + y) & 1) !== 0) continue; cmp++;
        let d = 0; for (let k = 0; k < 3; ++k) d = Math.max(d, Math.abs(px(s, x, y, k) - px(m, x + ${k}, y, k)));
        if (d > 1) allBad++;
        let flat = true; for (let dy = -1; dy <= 1 && flat; ++dy) for (let dx = -1; dx <= 1; ++dx)
          for (let k = 0; k < 3; ++k) if (px(m, x + ${k} + dx, y + dy, k) !== px(m, x + ${k}, y, k)) flat = false;
        if (flat && d > 1) { flatBad++; flatMax = Math.max(flatMax, d); } }
      return [cmp, flatBad, flatMax, allBad]; })()`);
    // Measured: 0 flat and 33 in all when correct; dropping the shift from the lighting passes
    // gave 153 flat and 8,189 in all, so the second bound is the one that catches it.
    check(flatBad === 0 && allBad <= cmp * 0.001,
      `lit left eye equals a mono render from the eye shifted ${k} px: ${flatBad} of ${cmp} flat pixels differ (max ${flatMax}), ${allBad} in all`);
    for (const box of ['lightDaylight', 'lightShadows', 'lightOcclusion', 'lightIndirect', 'lightSky']) await set(box, false);
    await set('stereoConvergeOnTarget', true);
  }

  await set('stereoMode', 'mono');
  await capture('mono-again');
  const [nBack] = await differ('mono', 'mono-again');
  check(nBack <= total * 0.001, `back to mono: ${nBack} pixels differ from the first mono capture`);
  const stored = await ev(`localStorage.getItem('agro.stereo')`);
  check(stored && !stored.includes('testPattern'), `stored per browser without the test pattern: ${stored}`);

  const errors = logs.filter(l => l.level === 'error' || l.level === 'exception').map(l => l.text.split('\n')[0].slice(0, 200));
  if (errors.length) console.log('console errors:', JSON.stringify(errors, null, 2));
  console.log(failures === 0 ? 'STEREO OK' : `${failures} FAILED`);
} finally {
  page?.close();
  await shutdown(browser);
}
process.exit(failures === 0 ? 0 : 1);
