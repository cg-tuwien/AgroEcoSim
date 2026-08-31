// Builds an actual dense canopy and measures what the second bounce does to it (phase 16 gate).
//
// The saved fixture holds one small geranium, whose leaves mostly see sky - there is very
// little for a second bounce to bounce off, so it reports a couple of percent. The plan's
// figure assumes a canopy dense enough that a leaf's neighbours are what it sees. This grows
// one: a small field, seeds on a tight raster, and enough simulated time for them to close.
import { launch, newPage, collectDiagnostics, lightingScript, sleep, shutdown } from './cdp.mjs';

const url = process.argv[2];
const HOURS = process.env.HOURS || '2400';
const TICK = process.env.TICK || '12';
const SPACING = process.env.SPACING || '0.35';
const FIELD = process.env.FIELD || '2';
const WAIT_MS = +(process.env.WAIT_MS || 600000);

const browser = await launch(+(process.env.CDP_PORT || 9780));
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
  await sleep(4000);

  const ev = async (e, awaitPromise = false) => {
    const r = await page.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };

  const setup = await ev(`(() => {
    const fire = el => { el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); };
    const set = (n, v) => { const el = document.querySelector('[name=' + n + ']'); if (!el) return 'missing ' + n;
      el.value = String(v); fire(el); return 'ok'; };
    const click = n => { const el = document.querySelector('[name=' + n + ']'); if (!el) return 'missing ' + n; el.click(); return 'ok'; };
    return {
      x: set('fieldSizeX', ${FIELD}), z: set('fieldSizeZ', ${FIELD}), d: set('fieldSizeD', 0.4),
      hours: set('totalhours', ${HOURS}), tick: set('hourspertick', ${TICK}),
      clear: click('clear-seeds'),
      spacing: set('seeds-optimal-distance', ${SPACING}),
      raster: click('seeds-dist'),
    };
  })()`);
  await sleep(2000);
  const seeded = await ev(`document.querySelectorAll('.scrollable-listing li').length`);

  await ev(`(() => { const b=Array.from(document.querySelectorAll('button')).find(x=>x.textContent.trim()==='Start'); if(!b) return 'missing'; b.click(); return 'clicked'; })()`);

  //Poll for the run to finish rather than sleeping a fixed time: growth time is not predictable.
  const started = Date.now();
  let done = false, lastSlider = '0';
  while (Date.now() - started < WAIT_MS) {
    await sleep(5000);
    const st = await ev(`(() => {
      const s = document.querySelector('[name=frameslider]');
      const b = Array.from(document.querySelectorAll('button')).find(x => x.textContent.trim() === 'Start');
      return { slider: s ? s.value : null, max: s ? s.max : null, busy: b ? b.disabled : null };
    })()`);
    lastSlider = st.slider;
    if (st.busy === false && st.slider !== '0' && st.slider === st.max) { done = true; break; }
  }
  await sleep(3000);
  await ev(`(() => { const b=document.querySelector('button[title="Seek to end"]'); if(b) b.click(); return 1; })()`);
  await sleep(4000);

  const scene = await ev(`(() => {
    const t = document.body.innerText.match(/(\\d+)\\s*objects/i);
    return { text: t ? t[1] : null };
  })()`);

  const setTier = async tier => {
    await ev(lightingScript(tier));
    await sleep(6000);
    return ev(`(async () => {
      const r = window.__renderer;
      if (!r || !r.readVoxelMeanRadiance) return null;
      const m = await r.readVoxelMeanRadiance();
      const n = r.readVoxelOccupancy ? await r.readVoxelOccupancy() : null;
      return m ? { r: m.r, g: m.g, b: m.b, sigma: m.sigma, occupied: n } : null;
    })()`, true);
  };

  const one = await setTier(3);
  const two = await setTier(4);
  const lum = m => m ? (m.r + m.g + m.b) / 3 : 0;
  const gain = lum(one) > 0 ? lum(two) / lum(one) : null;

  console.log(JSON.stringify({
    setup, seeded, finished: done, lastSlider, scene,
    oneBounce: one, twoBounces: two,
    percentBrighter: gain === null ? null : +((gain - 1) * 100).toFixed(1),
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception').map(l => l.text.split('\n')[0].slice(0, 160)).slice(0, 5),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
