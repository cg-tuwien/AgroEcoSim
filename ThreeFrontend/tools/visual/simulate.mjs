// Drives a real simulation through the UI, then screenshots the result.
// Determinism matters because the output is compared across two builds:
//   - the seed is fixed (initNumber 42, randomize off) so the sim is reproducible
//   - "Exact preview" forces the server to deliver every step instead of skipping the ones
//     a slow client misses, which would otherwise make history contents timing-dependent
//   - we seek to the LAST frame, which always arrives in the final `result` message
import { launch, newPage, collectDiagnostics, screenshot, sleep, shutdown } from './cdp.mjs';

const url = process.argv[2];
const outPrefix = process.argv[3];
const totalHours = +(process.env.TOTAL_HOURS || 240);
const mappings = (process.env.MAPPINGS || 'natural').split(',');

const browser = await launch(+(process.env.CDP_PORT || 9336));
let page;
const results = {};
try {
  page = await newPage(browser.port);
  await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable');
  await page.send('Log.enable');
  await page.send('Page.enable');
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });

  const loaded = new Promise(res => page.on(m => { if (m.method === 'Page.loadEventFired') res(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(15000)]);
  await sleep(2500);

  const evaluate = async (expression, awaitPromise = false) => {
    const r = await page.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };

  // Preact binds onChange, which React-style maps to the DOM 'change' event.
  const setup = await evaluate(`(() => {
    // preact/compat aliases onChange to the INPUT event, exactly as React does, so a bare
    // 'change' updates the DOM value but never reaches the handler and the signal keeps its
    // old value. Fire both so numbers, checkboxes and selects all take.
    const fire = (el) => {
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
    };
    const setNum = (name, v) => {
      const el = document.querySelector('input[name=' + name + ']');
      if (!el) return 'missing ' + name;
      el.value = String(v); fire(el); return 'ok';
    };
    const setCheck = (name, v) => {
      const el = document.querySelector('input[name=' + name + ']');
      if (!el) return 'missing ' + name;
      if (el.checked !== v) { el.checked = v; fire(el); }
      return 'ok';
    };
    const setSelect = (name, v) => {
      const el = document.querySelector('select[name=' + name + ']');
      if (!el) return 'missing ' + name;
      el.value = String(v); fire(el); return 'ok';
    };
    return {
      totalHours: setNum('totalhours', ${totalHours}),
      // Deliberately OFF. With ExactPreview the hub loop only advances when the client has
      // called Preview(), but SignalR's MaximumParallelInvocationsPerClient defaults to 1,
      // so that call queues behind the still-running Start() and the simulation deadlocks.
      // Determinism is preserved anyway: we screenshot the FINAL frame, which arrives in the
      // Result message anduses a fixed seed, not a timing-dependent preview.
      exactPreview: setCheck('exactPreview', false),
      // Ambient: the Mitsuba container is not running, and letting the backend attempt it
      // just burns time on a connection that will be refused.
      renderMode: setSelect('renderMode', 0),
      // The bundled default seed references a species literally named "default", which the
      // backend falls back to and which barely grows. Point it at a real species so the
      // comparison exercises leaves, petioles and per-species leaf meshes.
      seedSpecies: (() => {
        const el = document.querySelector('select[name=seedspec-0]');
        if (!el) return 'missing seedspec-0';
        const want = ${JSON.stringify(process.env.SPECIES || 'Geranium Macrorrhizum')};
        const opt = Array.from(el.options).find(o => o.value === want);
        if (!opt) return 'no option ' + want + ' in [' + Array.from(el.options).map(o => o.value).join('|') + ']';
        el.value = want; fire(el); return el.value;
      })(),
      // The default camera looks at the world origin, but the default seed sits at the
      // middle of the field (5, 5), so the plant would grow off-screen. Move it under the
      // camera target instead of trying to drive OrbitControls from the DOM.
      seedX: setNum('seedpx-0', 0),
      seedZ: setNum('seedpz-0', 0),
      speciesLoaded: document.querySelectorAll('#hud select option').length,
      // Read back what the app actually believes, not just what we typed.
      confirmTotalHours: document.querySelector('label[for=totalhours].unit')?.textContent,
    };
  })()`);
  results.setup = setup;

  await sleep(500);
  results.startClicked = await evaluate(`(() => {
    const b = document.querySelector('button[title=Simulate]');
    if (!b) return 'no simulate button';
    b.click(); return 'clicked';
  })()`);

  // Wait for the run to finish: the button flips to the stop glyph while computing.
  let waited = 0;
  let status = null;
  while (waited < 240000) {
    await sleep(1000); waited += 1000;
    status = await evaluate(`(() => {
      const b = document.querySelector('button[title=Simulate]');
      const t = document.getElementById('hud').innerText;
      const g = re => { const m = t.match(re); return m ? m[1] : null; };
      const prog = document.querySelector('#hud progress');
      return { computing: b ? b.innerText.trim() !== '\\u{1F680}' : null,
               objects: g(/Objects:\\s*(\\d+)/), triangles: g(/Triangles:\\s*(\\d+)/),
               mb: g(/([\\d.]+) Mb/),
               progress: prog ? prog.value + '/' + prog.max : null,
               slider: (document.querySelector('input[name=frameslider]')||{}).value };
    })()`);
    if (process.env.VERBOSE) console.error('t=' + waited + 'ms ' + JSON.stringify(status));
    if (status && status.computing === false && +status.objects > 0) break;
  }
  results.afterRun = { waitedMs: waited, ...status };

  // Seek to the final frame - the only one guaranteed to exist regardless of preview pacing.
  await evaluate(`(() => { const b = document.querySelector('button[title="Seek to end"]'); if (b) b.click(); })()`);
  await sleep(2500);

  // Dolly in on the plant. CDP's synthetic mouseWheel does not reach OrbitControls here,
  // so dispatch a real WheelEvent on the canvas - OrbitControls does not check isTrusted.
  // A fixed number of identical steps keeps the camera deterministic across builds.
  const zoomSteps = +(process.env.ZOOM_STEPS || 0);
  if (zoomSteps) {
    const moved = await evaluate(`(() => {
      const c = document.querySelector('#main3Dviewport canvas');
      if (!c) return 'no canvas';
      for (let i = 0; i < ${zoomSteps}; ++i)
        c.dispatchEvent(new WheelEvent('wheel', { deltaY: -120, deltaMode: 0, bubbles: true, cancelable: true, clientX: 640, clientY: 500 }));
      return 'dispatched';
    })()`);
    results.zoom = { steps: zoomSteps, moved };
    await sleep(800);
  }

  for (const m of mappings) {
    const applied = await evaluate(`(() => {
      const el = document.querySelector('select[name=visualmapping]');
      if (!el) return 'missing';
      el.value = ${JSON.stringify(m)};
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
      return el.value;
    })()`);
    await sleep(1200);
    await screenshot(page, `${outPrefix}-${m}.png`);
    results[`mapping_${m}`] = applied;
  }

  const finalProbe = await evaluate(`(() => {
    const t = document.getElementById('hud').innerText;
    const g = re => { const m = t.match(re); return m ? m[1] : null; };
    const c = document.querySelector('#main3Dviewport canvas');
    return { objects: g(/Objects:\\s*(\\d+)/), triangles: g(/Triangles:\\s*(\\d+)/),
             time: g(/([\\d]+) h \\(/), canvas: c ? c.width + 'x' + c.height : null };
  })()`);
  results.final = finalProbe;
  results.errors = logs.filter(l => l.level === 'error' || l.level === 'exception')
                       .map(l => l.text.split('\n')[0].slice(0, 140));
  console.log(JSON.stringify(results, null, 2));
} finally {
  page?.close();
  await shutdown(browser);
}
