// Checks the sky background against the PHYSICS, not against a previous screenshot.
//
// Reads the dome back through the debug hook, then samples the rendered canvas and reports
// both. A screenshot diff can only say that something changed; this says whether what changed
// is the sky the simulation is integrating, and it is the only check that ties the pixels on
// screen to Agro/Irradiance/SkyDome.cs rather than to the last known-good image.
//
// Deliberately loads NO session. A background needs no scene, which skips the file-chooser
// dance that makes the other harness scripts slow and fragile, and makes this usable against
// either backend in a few seconds.
//
// env: DATE (start date), STEP (timestep), TICK (hours per tick), COVERAGE (0..1)
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';
import { writeFileSync } from 'node:fs';

const url = process.argv[2];
const DATE = process.env.DATE || '2026-06-21';
const STEP = +(process.env.STEP || 12);
const TICK = +(process.env.TICK || 1);
const COVERAGE = +(process.env.COVERAGE || 0);
//SKY=0 checks the OTHER half of the contract: switched off, the background must be the flat
//theme colour and nothing else may have moved.
const SKY = process.env.SKY !== '0';
const LAT = process.env.LAT, LON = process.env.LON, AZ = process.env.AZ, ALT = process.env.ALT;

const browser = await launch(+(process.env.CDP_PORT || 21400));
let page;
try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable'); await page.send('Page.enable');
  //The app registers a service worker, which will happily serve the PREVIOUS build's bundle
  //after a rebuild - so a harness run can silently test code that is no longer on disk.
  await page.send('Network.enable');
  await page.send('Network.setBypassServiceWorker', { bypass: true });
  await page.send('Network.setCacheDisabled', { cacheDisabled: true });
  await page.send('Emulation.setDeviceMetricsOverride',
    { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });

  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(20000)]);
  await sleep(4000);

  const ev = async e => {
    const r = await page.send('Runtime.evaluate',
      { expression: e, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails)
      throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };

  if (!await ev('!!window.__renderer && !!window.__appstate')) {
    //Say WHICH of the three ways this fails, because they need different fixes: no app at all,
    //an app whose viewport never mounted a backend, or a mounted backend with the flag off.
    const why = await ev(`JSON.stringify({ search: location.search,
      appMounted: !!document.querySelector('#app'),
      viewport: !!document.querySelector('#main3Dviewport'),
      canvas: !!document.querySelector('#main3Dviewport canvas'),
      appstate: !!window.__appstate, renderer: !!window.__renderer })`);
    throw new Error(`no debug hook: ${why}\n` + logs.slice(-6).map(l => l.text.slice(0, 200)).join('\n'));
  }

  // Driven through appstate rather than the DOM: the start date, the playhead and the weather
  // are not reachable from controls on one tab, and the subject here is the sky, not the HUD.
  await ev(`(() => { const a = window.__appstate;
    a.initialDate.value = ${JSON.stringify(DATE)};
    a.simHoursPerTick = ${TICK};
    a.weather.value = ${COVERAGE > 0 ? `new Float32Array(4096).fill(${COVERAGE})` : 'undefined'};
    a.history.length = 0;
    for (let t = 0; t <= ${STEP}; ++t) a.history.push({ t, s: undefined, plain: true });
    a.historyCount.value = a.history.length;
    a.playPointer.value = ${STEP};
    a.lightSky.value = ${SKY};
    ${LAT !== undefined ? `a.latitude.value = ${+LAT};` : ''}
    ${LON !== undefined ? `a.longitude.value = ${+LON};` : ''}
    //--az means "put the EFFECTIVE azimuth at N": write the base and pin the orientation to
    //Backward (index 2 in AZIMUTH_OFFSETS), the zero-offset case, so the base is the whole story.
    ${AZ !== undefined ? `a.azimuthBase.value = ${+AZ}; a.azimuthOffset.value = 2;` : ''}
    ${ALT !== undefined ? `a.altitude.value = ${+ALT};` : ''}
    return 1; })()`);

  //Aim the camera. The DEFAULT pose looks 6 degrees BELOW the horizon - it is framed on the
  //field - so every pixel on screen is the dome's ground half and a check that sampled it
  //would report the soil colour and call it the sky.
  //
  //Looking towards -Z is also what makes the east/west check readable: with world up, the
  //camera's right vector is then +X, so screen-RIGHT is east. At a morning step the right of
  //the image must be the bright side.
  await ev(`(() => { const r = window.__renderer;
    r.setCameraPose({ position: { x: 0, y: 2, z: 8 }, target: { x: 0, y: 3.5, z: 0 },
                      up: { x: 0, y: 1, z: 0 }, fovY: 50, near: 0.05, far: 500 });
    return 1; })()`);
  await sleep(2500);

  const dome = SKY ? await ev(`(() => { const c = window.__renderer.skyCube;
    if (!c) return null;
    const d = c.dome;
    return { revision: c.revision, size: c.size,
      globalHorizontal: +d.globalHorizontal.toFixed(1),
      diffuseHorizontal: +d.diffuseHorizontal.toFixed(1),
      sunY: +d.sunDirection.y.toFixed(3), sunX: +d.sunDirection.x.toFixed(3),
      sunZ: +d.sunDirection.z.toFixed(3) }; })()`) : null;
  if (SKY && !dome) throw new Error('the renderer has no sky cube - the switch never reached it');
  if (!SKY && await ev('!!window.__renderer.skyCube'))
    throw new Error('the sky is switched off but the renderer still holds a cube');

  // Sample the rendered image. Via Page.captureScreenshot and NOT by drawing the viewport
  // canvas into a 2D context: neither backend asks for preserveDrawingBuffer, so reading the
  // drawing buffer outside a frame yields all zeroes - which looks exactly like a black sky.
  const shot = (await page.send('Page.captureScreenshot', { format: 'png' })).data;
  if (process.env.OUT) writeFileSync(process.env.OUT, Buffer.from(shot, 'base64'));
  const pixels = await ev(`(async () => {
    const img = await new Promise((res, rej) => { const i = new Image();
      i.onload = () => res(i); i.onerror = rej;
      i.src = 'data:image/png;base64,${shot}'; });
    const g = document.createElement('canvas');
    g.width = img.width; g.height = img.height;
    const ctx = g.getContext('2d');
    ctx.drawImage(img, 0, 0);
    const px = ctx.getImageData(0, 0, g.width, g.height).data;
    const at = (fx, fy) => {
      const x = Math.round(fx * (g.width - 1)), y = Math.round(fy * (g.height - 1));
      const i = (y * g.width + x) * 4;
      return [px[i], px[i + 1], px[i + 2]];
    };
    //The HUD occupies the left quarter of the page, so every sample sits to the right of it.
    return { canvas: g.width + 'x' + g.height,
             zenith: at(0.62, 0.04),
             west: at(0.40, 0.22), east: at(0.97, 0.22),
             belowHorizon: at(0.62, 0.99) };
  })()`);

  //BENCH=1: how much the sky actually costs during playback. The sky is rebuilt whenever the
  //timestep changes, which during playback is every frame, so this is the number that decides
  //whether SKY_FACE_SIZE is affordable. Measured in the real bundle in a real browser - a
  //jest figure for the same loop is an order of magnitude out, because ts-jest's transpilation
  //dominates it.
  const bench = process.env.BENCH === '1' ? await ev(`(() => {
    const a = window.__appstate;
    const run = (on) => {
      a.lightSky.value = on;
      for (let i = 0; i < 20; ++i) a.playPointer.value = i % ${STEP || 1};   //warm up
      const t0 = performance.now();
      const laps = 120;
      for (let i = 0; i < laps; ++i) a.playPointer.value = i % ${STEP || 1};
      return (performance.now() - t0) / laps;
    };
    const off = run(false), on = run(true);
    return { msPerStepSkyOff: +off.toFixed(3), msPerStepSkyOn: +on.toFixed(3),
             skyCostMs: +(on - off).toFixed(3) };
  })()`) : undefined;

  console.log(JSON.stringify({
    backend: await ev('window.__renderer.kind'),
    sky: SKY, date: DATE, step: STEP, tick: TICK, coverage: COVERAGE,
    site: { lat: LAT, lon: LON, az: AZ, alt: ALT }, dome, pixels, bench,
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0, 160))
      .filter(t => !/CONNECTION_REFUSED|Failed to fetch|SimSocket|signalr|WebSocket/i.test(t))
      .slice(0, 4),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
