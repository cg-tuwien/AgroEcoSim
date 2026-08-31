// Loads the fixture, runs the animation export, and captures the produced file.
import { launch, newPage, collectDiagnostics, lightingScript, sleep, shutdown } from './cdp.mjs';
import { writeFileSync } from 'node:fs';

const [url, sessionPath, outPath] = process.argv.slice(2);
const FORMAT = process.env.FORMAT || 'webm';
const W = process.env.W || '640', H = process.env.H || '400', FPS = process.env.FPS || '10';

const browser = await launch(+(process.env.CDP_PORT || 9900));
let page;
try {
  page = await newPage(browser.port);
  await page.ready;
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

  const chooser = new Promise(r => page.on(m => { if (m.method === 'Page.fileChooserOpened') r(m.params); }));
  await page.send('Page.setInterceptFileChooserDialog', { enabled: true });
  const pt = await ev(`(() => { const b=Array.from(document.querySelectorAll('button')).find(x=>x.textContent.trim()==='Load'); const r=b.getBoundingClientRect(); return {x:Math.round(r.left+r.width/2),y:Math.round(r.top+r.height/2)}; })()`);
  for (const type of ['mousePressed','mouseReleased'])
    await page.send('Input.dispatchMouseEvent', { type, x: pt.x, y: pt.y, button: 'left', clickCount: 1 });
  const ch = await Promise.race([chooser, sleep(8000).then(()=>null)]);
  if (!ch) throw new Error('no file chooser');
  await page.send('DOM.setFileInputFiles', { files: [sessionPath], backendNodeId: ch.backendNodeId });
  await sleep(6000);

  const cfg = await ev(`(() => {
    const fire = el => { el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); };
    const set = (n,v) => { const el=document.querySelector('[name='+n+']'); if(!el) return 'missing '+n; el.value=String(v); fire(el); return 'ok'; };
    return { w:set('offlineWidth', ${W}), h:set('offlineHeight', ${H}), ssaa:set('offlineSsaa',1),
             fps:set('offlineFps', ${FPS}), fmt:set('offlineFormat', ${JSON.stringify(FORMAT)}),
             timing:set('offlineTiming', ${JSON.stringify(process.env.TIMING || 'uniform')}),
             hps: ${JSON.stringify(process.env.TIMING || 'uniform')} === 'simTime'
                  ? set('offlineHoursPerSecond', ${process.env.HOURS_PER_SECOND || 720}) : 'n/a' };
  })()`);
  await sleep(800);

  const frames = await ev(`(() => { const s=document.body.innerText.match(/(\\d+) frames from (\\d+) stored/); return s? s[0] : null; })()`);

  await ev(`(() => {
    window.__out = null;
    const orig = URL.createObjectURL.bind(URL);
    URL.createObjectURL = b => { window.__out = b; return orig(b); };
    return 1;
  })()`);

  const tierSet = process.env.TIER === undefined || process.env.TIER === ''
    ? 'default' : await ev(lightingScript(process.env.TIER));
  await sleep(1500);

  await ev(`(() => { const b=document.querySelector('button[name=renderAnimation]'); if(!b) return 'missing'; if(b.disabled) return 'disabled'; b.click(); return 'clicked'; })()`);

  let waited = 0, status = null;
  while (waited < 180000) {
    await sleep(1000); waited += 1000;
    status = await ev(`(() => { const b=document.querySelector('button[name=renderAnimation]'); return { busy: b ? b.disabled : null, text: (document.body.innerText.match(/(\\d+)\\/(\\d+) · \\w+/)||[])[0] || null, done: /Saved [\\d.]+ MB/.test(document.body.innerText) }; })()`);
    if (status.done || (status.busy === false && waited > 3000)) break;
  }

  const b64 = await ev(`(async () => {
    if (!window.__out) return null;
    const buf = await window.__out.arrayBuffer();
    let s=''; const v=new Uint8Array(buf);
    const CH=0x8000;
    for (let i=0;i<v.length;i+=CH) s += String.fromCharCode.apply(null, v.subarray(i,i+CH));
    return btoa(s);
  })()`, true);

  if (b64) writeFileSync(outPath, Buffer.from(b64, 'base64'));
  console.log(JSON.stringify({ tierSet, cfg, frames, status,
    bytes: b64 ? Buffer.from(b64,'base64').length : 0,
    errors: logs.filter(l=>l.level==='error'||l.level==='exception').map(l=>l.text.split('\n')[0].slice(0,140)) }, null, 1));
} finally { page?.close(); await shutdown(browser); }
