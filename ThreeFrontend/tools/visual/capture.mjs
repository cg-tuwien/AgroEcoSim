// Loads the app in headless chromium, waits for the viewport to settle, reports any
// console errors, probes the live DOM for renderer health, and screenshots.
import { launch, newPage, collectDiagnostics, screenshot, sleep, shutdown } from './cdp.mjs';

const url = process.argv[2];
const out = process.argv[3];
const width = +(process.argv[4] || 1280);
const height = +(process.argv[5] || 800);
const settleMs = +(process.env.SETTLE_MS || 4000);

const browser = await launch(+(process.env.CDP_PORT || 9333));
let page;
try {
  page = await newPage(browser.port);
  await page.ready;

  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable');
  await page.send('Log.enable');
  await page.send('Page.enable');
  await page.send('Emulation.setDeviceMetricsOverride', {
    width, height, deviceScaleFactor: 1, mobile: false,
  });

  const loaded = new Promise(res => page.on(m => { if (m.method === 'Page.loadEventFired') res(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(15000)]);
  await sleep(settleMs);

  // Probe the running app through the DOM. The HUD prints live counters straight from
  // appstate, so they double as a readout of what the renderer was handed.
  const probe = await page.send('Runtime.evaluate', {
    returnByValue: true,
    expression: `(() => {
      const viewport = document.getElementById('main3Dviewport');
      const canvas = viewport && viewport.querySelector('canvas');
      let gl = null, glVersion = null;
      if (canvas) {
        gl = canvas.getContext('webgl2') || canvas.getContext('webgl');
        glVersion = gl ? gl.getParameter(gl.VERSION) : null;
      }
      const text = document.getElementById('hud') ? document.getElementById('hud').innerText : '';
      const grab = re => { const m = text.match(re); return m ? m[1].trim() : null; };
      return {
        hasViewport: !!viewport,
        hasCanvas: !!canvas,
        canvasW: canvas ? canvas.width : 0,
        canvasH: canvas ? canvas.height : 0,
        glVersion,
        objects: grab(/Objects:\\s*(\\d+)/),
        triangles: grab(/Triangles:\\s*(\\d+)/),
        seeds: grab(/Seeds:\\s*(\\d+)/),
        obstacles: grab(/Obstacles:\\s*(\\d+)/),
        backendRenderer: grab(/Backend Renderer:\\s*(\\S*)/),
      };
    })()`,
  });

  await screenshot(page, out);

  const errors = logs.filter(l => l.level === 'error' || l.level === 'exception');
  console.log(JSON.stringify({ probe: probe.result.value, errors, allLogs: logs }, null, 2));
} finally {
  page?.close();
  await shutdown(browser);
}
