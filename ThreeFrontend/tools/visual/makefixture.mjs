// Runs a simulation and captures the app's own Save output, so the same multi-frame history
// can be replayed identically across builds. Live runs cannot be compared: the backend keeps
// state between runs, so two identical requests grow different plants.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';
import { writeFileSync } from 'node:fs';

const [url, outPath] = process.argv.slice(2);
const SIM = process.env.SIMULATE || '960', TICK = process.env.TICK || '4';

const browser = await launch(+(process.env.CDP_PORT || 19850));
let page;
try {
  page = await newPage(browser.port); await page.ready;
  collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable'); await page.send('Page.enable');
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(15000)]);
  await sleep(2500);

  const ev = async (e, awaitPromise = false) => {
    const r = await page.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };

  await ev(`(() => { const fire = el => { el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); };
    const set = (n,v) => { const el=document.querySelector('[name='+n+']'); el.value=String(v); fire(el); };
    set('totalhours', ${SIM}); set('hourspertick', ${TICK});
    //Without a date the backend defaults to 1 February, and a February sun at 48 degrees north
    //grows almost nothing - a 960-hour run produced a plant of 2e-6 cubic metres. Any fixture
    //meant to carry real geometry has to start in the growing season.
    const d = document.querySelector('[name=initialDate]');
    if (d) { d.value = ${JSON.stringify(process.env.START_DATE || '2026-05-01')}; fire(d); }
    return d ? 'dated' : 'no date field'; })()`);
  await ev(`(() => { document.querySelector('button[title=Simulate]').click(); return 1; })()`);

  const deadline = Date.now() + 600000;
  let frames = 0;
  while (Date.now() < deadline) {
    await sleep(2000);
    const st = await ev(`(() => { const b=document.querySelector('button[title=Simulate]');
      return { computing: b ? b.innerText.trim() !== '\\u{1F680}' : null,
               frames: +((document.querySelector('input[name=frameslider]')||{}).max || 0) }; })()`);
    frames = st.frames;
    if (st.computing === false && frames > 1) break;
  }
  await sleep(2000);

  await ev(`(() => { window.__saved = null; const orig = URL.createObjectURL.bind(URL);
    URL.createObjectURL = b => { window.__saved = b; return orig(b); }; return 1; })()`);
  const btn = await ev(`(() => { const b=[...document.querySelectorAll('button')].find(x => /save/i.test(x.title||'') || x.textContent.trim()==='Save');
    if (!b) return 'no save button: ' + [...document.querySelectorAll('button')].map(x=>x.title||x.textContent.trim()).join('|');
    b.click(); return 'clicked'; })()`);
  if (btn !== 'clicked') throw new Error(btn);
  await sleep(3000);

  const text = await ev(`(async () => window.__saved ? await window.__saved.text() : null)()`, true);
  if (!text) throw new Error('no save blob captured');
  writeFileSync(outPath, text);
  console.log(JSON.stringify({ frames, bytes: text.length, historyEntries: JSON.parse(text).history.length }, null, 1));
} finally { page?.close(); await shutdown(browser); }
