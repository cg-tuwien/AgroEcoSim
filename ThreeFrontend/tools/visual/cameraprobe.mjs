// Replays a pointer script on the web viewport and prints the camera pose it ends at, so the Godot viewer's
// camera can be compared with the web's number for number (GodotFrontend: --replay-input SCRIPT --dump-pose).
//
//   CHROME=/snap/bin/chromium node cameraprobe.mjs "http://localhost:8080/?debughook=1&gpu=0" script.json
//
// Script: {"viewport":{"w":1280,"h":800},"events":[{"t":"down","b":0,"x":700,"y":400,"mods":["shift"]},
//          {"t":"move","x":720,"y":405},{"t":"up","b":0,"x":720,"y":405},{"t":"wheel","dy":100}]}
// Coordinates are viewport pixels; the side panel covers the left quarter, so keep x beyond it.
import { launch, newPage, sleep, shutdown } from './cdp.mjs';
import { readFileSync } from 'node:fs';

const [url, scriptPath] = process.argv.slice(2);
const script = JSON.parse(readFileSync(scriptPath, 'utf8'));
const W = script.viewport?.w ?? 1280, H = script.viewport?.h ?? 800;
const browser = await launch(+(process.env.CDP_PORT || 9710));
try {
  const page = await newPage(browser.port);
  await page.ready;
  await page.send('Page.enable');
  await page.send('Runtime.enable');
  await page.send('Emulation.setDeviceMetricsOverride', { width: W, height: H, deviceScaleFactor: 1, mobile: false });
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(15000)]);
  await sleep(2500);
  const ev = async e => {
    const r = await page.send('Runtime.evaluate', { expression: e, returnByValue: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };
  const rect = await ev(`(() => { const r = document.querySelector('#main3Dviewport').getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; })()`);
  const buttons = ['left', 'middle', 'right'];
  const mask = [1, 4, 2];
  const modBits = m => (m || []).reduce((a, k) => a | ({ alt: 1, ctrl: 2, meta: 4, shift: 8 }[k] || 0), 0);
  let held = -1, mods = 0;
  for (const e of script.events) {
    if (e.t === 'down') {
      held = e.b; mods = modBits(e.mods);
      await page.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: rect.x + e.x, y: rect.y + e.y, button: buttons[e.b], buttons: mask[e.b], clickCount: 1, modifiers: mods });
    } else if (e.t === 'move') {
      await page.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: rect.x + e.x, y: rect.y + e.y, button: held >= 0 ? buttons[held] : 'none', buttons: held >= 0 ? mask[held] : 0, modifiers: mods });
    } else if (e.t === 'up') {
      await page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: rect.x + e.x, y: rect.y + e.y, button: buttons[e.b], buttons: 0, clickCount: 1, modifiers: mods });
      held = -1; mods = 0;
    } else if (e.t === 'wheel') {
      await page.send('Input.dispatchMouseEvent', { type: 'mouseWheel', x: rect.x + W * 0.6, y: rect.y + H * 0.5, deltaX: 0, deltaY: e.dy });
    }
    await sleep(30);
  }
  await sleep(300);
  const pose = await ev(`JSON.stringify(window.__renderer.getCameraPose())`);
  console.log(JSON.stringify({ viewport: rect, pose: JSON.parse(pose) }));
} finally {
  await shutdown(browser);
}
