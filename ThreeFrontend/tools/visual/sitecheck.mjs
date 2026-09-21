// End-to-end check that the SERVER's site and weather reach the viewport.
//
// Runs a short real simulation and then reads back what the `runSite` message delivered: the
// time zone the server derived from the coordinates, and the cloud cover it planned. Both are
// things the browser cannot compute for itself, so this is the only way to know they arrived.
//
// It also drives the four Terrain-tab inputs through the DOM, so the controls themselves are
// exercised rather than the signals behind them.
//
// env: TOTAL_HOURS, TICK, LAT, LON, ALT, AZ
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';

const url = process.argv[2];
const TOTAL_HOURS = +(process.env.TOTAL_HOURS || 72);
const TICK = +(process.env.TICK || 1);
const LAT = process.env.LAT, LON = process.env.LON, ALT = process.env.ALT, AZ = process.env.AZ;

const browser = await launch(+(process.env.CDP_PORT || 24100));
let page;
try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable'); await page.send('Page.enable');
  await page.send('Network.enable');
  await page.send('Network.setBypassServiceWorker', { bypass: true });
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

  if (!await ev('!!window.__appstate')) throw new Error('no debug hook - URL needs ?debughook=1');

  // Through the DOM, not the signals: this is also the test that the four new controls exist,
  // are reachable on the Terrain tab, and accept a typed value. preact/compat aliases onChange
  // to the INPUT event, so both events have to be fired or the signal keeps its old value.
  const setup = await ev(`(() => {
    const fire = el => { el.dispatchEvent(new Event('input', {bubbles:true}));
                         el.dispatchEvent(new Event('change', {bubbles:true})); };
    const setNum = (name, v) => { const el = document.querySelector('input[name=' + name + ']');
      if (!el) return 'missing'; el.value = String(v); fire(el); return 'ok'; };
    const setCheck = (name, v) => { const el = document.querySelector('input[name=' + name + ']');
      if (!el) return 'missing'; if (el.checked !== v) { el.checked = v; fire(el); } return 'ok'; };
    const out = {};
    window.__appstate.activeTab.value = 'tab-terrain';
    return Promise.resolve().then(() => new Promise(r => setTimeout(r, 400))).then(() => {
      ${LAT !== undefined ? `out.lat = setNum('siteLatitude', ${+LAT});` : ''}
      ${LON !== undefined ? `out.lon = setNum('siteLongitude', ${+LON});` : ''}
      ${ALT !== undefined ? `out.alt = setNum('siteAltitude', ${+ALT});` : ''}
      ${AZ !== undefined ? `out.az = setNum('siteAzimuth', ${+AZ});` : ''}
      out.sky = setCheck('lightSky', true);
      out.read = { lat: window.__appstate.latitude.value, lon: window.__appstate.longitude.value,
                   alt: window.__appstate.altitude.value, az: window.__appstate.azimuth.value };
      return out;
    });
  })()`);

  // Deterministic and short. hoursPerTick snaps server-side, so read back what was accepted.
  await ev(`(() => { const a = window.__appstate;
    a.totalHours.value = ${TOTAL_HOURS}; a.hoursPerTick.value = ${TICK};
    a.randomize.value = false; a.initNumber.value = 42; a.exactPreview.value = true;
    return 1; })()`);
  await sleep(500);

  const started = await ev(`(() => {
    //The Start control is a rocket glyph, not the word "Start".
    const b = [...document.querySelectorAll('button')].find(x => x.textContent.trim() === '\u{1F680}');
    if (!b) return 'no start button: ' + [...document.querySelectorAll('button')].map(x => x.textContent.trim()).join('|');
    b.click(); return 'clicked';
  })()`);

  // Wait for the run to finish, or for the site message to arrive and the run to stall.
  let site = null;
  for (let i = 0; i < 60; ++i) {
    await sleep(2000);
    site = await ev(`(() => { const a = window.__appstate;
      const w = a.weather.value;
      return { runId: a.runId.value, step: a.simStep.value, length: a.simLength.value,
               timeZone: a.siteTimeZone.value || null,
               weatherLength: w ? w.length : 0,
               weatherMin: w ? Math.min(...w) : null, weatherMax: w ? Math.max(...w) : null,
               weatherMean: w ? (w.reduce((s,v) => s+v, 0) / w.length) : null,
               lat: a.latitude.value, lon: a.longitude.value, alt: a.altitude.value, az: a.azimuth.value,
               historyCount: a.historyCount.value }; })()`);
    if (site.weatherLength > 0 && site.historyCount > 2) break;
  }

  // What the sky actually shows for a cloudy step versus a clear one, taken from the SAME run.
  const contrast = await ev(`(() => { const a = window.__appstate, w = a.weather.value;
    if (!w || !a.history.length) return null;
    const steps = a.history.map((h, i) => ({ i, t: h.t, c: w[h.t] ?? 0 }))
      .filter(s => s.c !== undefined);
    if (!steps.length) return null;
    const clearest = steps.reduce((m, s) => s.c < m.c ? s : m);
    const dullest = steps.reduce((m, s) => s.c > m.c ? s : m);
    const read = (idx) => { a.playPointer.value = idx; return null; };
    return { clearest, dullest };
  })()`);

  const domeAt = async (historyIndex) => {
    await ev(`(() => { window.__appstate.playPointer.value = ${historyIndex}; return 1; })()`);
    await sleep(1200);
    return ev(`(() => { const c = window.__renderer && window.__renderer.skyCube;
      if (!c) return null;
      return { ghi: +c.dome.globalHorizontal.toFixed(1),
               dhi: +c.dome.diffuseHorizontal.toFixed(1),
               zenith: [...c.faces[2].slice((64*128+64)*4, (64*128+64)*4+3)] }; })()`);
  };

  const skies = contrast ? {
    clearest: { ...contrast.clearest, dome: await domeAt(contrast.clearest.i) },
    dullest: { ...contrast.dullest, dome: await domeAt(contrast.dullest.i) },
  } : null;

  console.log(JSON.stringify({ setup, started, site, skies,
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0, 200)).slice(0, 5),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
