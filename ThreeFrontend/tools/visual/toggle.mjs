// Screenshots the LIVE canvas before and after ticking a lighting checkbox.
//
// The offscreen still path (renderToPixels) and the interactive viewport are different code
// paths, and a bug reachable only from the interactive one will not show up in a still. WebGL
// composites in headless Chromium, so a real screenshot is possible here - WebGPU does not.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';
import { writeFileSync } from 'node:fs';

const [url, fixture, name] = process.argv.slice(2);
const TOGGLE = process.env.TOGGLE || 'lightOcclusion';
const PRESET = (process.env.PRESET || '').split(',').filter(Boolean);

const browser = await launch(+(process.env.CDP_PORT || 22000));
let page;
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

  const ev = async e => {
    const r = await page.send('Runtime.evaluate', { expression: e, returnByValue: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };

  const chooser = new Promise(r => page.on(m => { if (m.method === 'Page.fileChooserOpened') r(m.params); }));
  await page.send('Page.setInterceptFileChooserDialog', { enabled: true });
  const pt = await ev(`(() => { const b=[...document.querySelectorAll('button')].find(x=>x.textContent.trim()==='Load');
    const r=b.getBoundingClientRect(); return {x:Math.round(r.left+r.width/2),y:Math.round(r.top+r.height/2)}; })()`);
  for (const type of ['mousePressed','mouseReleased'])
    await page.send('Input.dispatchMouseEvent', { type, x: pt.x, y: pt.y, button: 'left', clickCount: 1 });
  const ch = await Promise.race([chooser, sleep(10000).then(()=>null)]);
  if (!ch) throw new Error('no file chooser');
  await page.send('DOM.setFileInputFiles', { files: [fixture], backendNodeId: ch.backendNodeId });
  await sleep(7000);
  await ev(`(() => { const b=document.querySelector('button[title="Seek to end"]'); if(b) b.click(); return 1; })()`);
  await sleep(3000);

  const setBox = (n, v) => ev(`(() => { const el=document.querySelector('[name=${n}]'); if(!el) return 'missing';
    if (el.checked !== ${v}) { el.checked = ${v};
      el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); }
    return el.checked; })()`);

  for (const p of PRESET) await setBox(p, true);
  await setBox(TOGGLE, false);
  await sleep(3000);

  const shot = async () => (await page.send('Page.captureScreenshot', { format: 'png' })).data;
  const before = await shot();

  const on = await setBox(TOGGLE, true);
  await sleep(3000);
  const after = await shot();

  const resolved = await ev(`(() => { const r = window.__renderer; if (!r) return 'no hook';
    return { kind: r.kind, boxOn: document.querySelector('[name=${TOGGLE}]').checked }; })()`);

  writeFileSync(`${name}-off.png`, Buffer.from(before, 'base64'));
  writeFileSync(`${name}-on.png`, Buffer.from(after, 'base64'));
  console.log(JSON.stringify({ toggle: TOGGLE, preset: PRESET, checkboxAccepted: on, resolved,
    identical: before === after,
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0,160)).filter(t => !/CONNECTION_REFUSED|Failed to fetch/.test(t)).slice(0,3),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
