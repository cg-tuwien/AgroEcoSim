// Loads a saved .json session through the app's own Load button and screenshots the result.
//
// A saved session is the strongest parity fixture available: it pins terrain, seeds, species
// and every view setting, with no simulation and therefore no timing dependence at all. Both
// builds are fed byte-identical input.
import { launch, newPage, collectDiagnostics, screenshot, sleep, shutdown } from './cdp.mjs';

const [url, sessionPath, outPath] = process.argv.slice(2);
const settle = +(process.env.SETTLE_MS || 12000);

const browser = await launch(+(process.env.CDP_PORT || 9350));
let page;
try {
  page = await newPage(browser.port);
  await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable');
  await page.send('Log.enable');
  await page.send('Page.enable');
  await page.send('DOM.enable');
  await page.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });

  const loaded = new Promise(res => page.on(m => { if (m.method === 'Page.loadEventFired') res(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(15000)]);
  await sleep(2500);

  // appstate.load() builds a hidden <input type=file> and clicks it, so the only way in is
  // to intercept the resulting chooser and hand it a path.
  const chooser = new Promise(res => page.on(m => { if (m.method === 'Page.fileChooserOpened') res(m.params); }));
  await page.send('Page.setInterceptFileChooserDialog', { enabled: true });

  // A script-initiated click carries no user activation, and Chrome silently refuses to
  // open a file chooser without it. Dispatch a real mouse event at the button's centre.
  const rect = await page.send('Runtime.evaluate', {
    returnByValue: true,
    expression: `(() => {
      const b = Array.from(document.querySelectorAll('button')).find(x => x.textContent.trim() === 'Load');
      if (!b) return null;
      const r = b.getBoundingClientRect();
      return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) };
    })()`,
  });
  const pt = rect.result.value;
  if (!pt) throw new Error('Load button not found');
  for (const type of ['mousePressed', 'mouseReleased'])
    await page.send('Input.dispatchMouseEvent', { type, x: pt.x, y: pt.y, button: 'left', clickCount: 1 });

  const ev = await Promise.race([chooser, sleep(8000).then(() => null)]);
  if (!ev) throw new Error('file chooser never opened');
  await page.send('DOM.setFileInputFiles', { files: [sessionPath], backendNodeId: ev.backendNodeId });

  await sleep(settle);

  // load() ends with SeekBackward, i.e. frame 0, which is empty. Jump to the last frame so
  // the fixture's actual plant is what gets rendered.
  await page.send('Runtime.evaluate', {
    expression: `(() => { const b = document.querySelector('button[title="Seek to end"]'); if (b) b.click(); return !!b; })()`,
    returnByValue: true,
  });
  await sleep(3000);

  // Optionally switch the visual mapping, which exercises the emissive ramps rather than
  // the natural albedo path.
  if (process.env.MAPPING) {
    await page.send('Runtime.evaluate', { returnByValue: true, expression: `(() => {
      const el = document.querySelector('select[name=visualmapping]');
      if (!el) return 'missing';
      el.value = ${JSON.stringify(process.env.MAPPING || 'natural')};
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
      return el.value;
    })()` });
    await sleep(2500);
  }

  const probe = await page.send('Runtime.evaluate', {
    returnByValue: true,
    expression: `(() => {
      const t = document.getElementById('hud').innerText;
      const g = re => { const m = t.match(re); return m ? m[1] : null; };
      const c = document.querySelector('#main3Dviewport canvas');
      return { seeds: g(/Seeds:\\s*(\\d+)/), obstacles: g(/Obstacles:\\s*(\\d+)/),
               objects: g(/Objects:\\s*(\\d+)/), triangles: g(/Triangles:\\s*(\\d+)/),
               canvas: c ? c.width + 'x' + c.height : null };
    })()`,
  });

  await screenshot(page, outPath);
  console.log(JSON.stringify({
    probe: probe.result.value,
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception').map(l => l.text.split('\n')[0].slice(0, 160)),
  }, null, 2));
} finally {
  page?.close();
  await shutdown(browser);
}
