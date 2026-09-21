// Measures sustained playback frame rate: runs a short simulation to build a history, then
// steps the playhead from the start to the end and back, timing every step from inside the
// page. Reports the median so a single stall does not move the number.
//
// env: SIMULATE (total simulated hours), TICK (hours per step), LAPS (traversals to time)
import { launch, newPage, collectDiagnostics, lightingScript, sleep, shutdown } from './cdp.mjs';

const url = process.argv[2];
const SIM = process.env.SIMULATE || '480';
const TICK = process.env.TICK || '4';
const LAPS = +(process.env.LAPS || 4);

const browser = await launch(+(process.env.CDP_PORT || 19800));
let page;
try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable'); await page.send('Page.enable');
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

  //A saved session replays identically across builds; a live run does not, because the
  //backend keeps state between runs and grows a different plant each time. Comparing two
  //builds therefore has to go through a fixture.
  if (process.env.FIXTURE) {
    const chooser = new Promise(r => page.on(m => { if (m.method === 'Page.fileChooserOpened') r(m.params); }));
    await page.send('Page.setInterceptFileChooserDialog', { enabled: true });
    const pt = await ev(`(() => { const b=[...document.querySelectorAll('button')].find(x=>x.textContent.trim()==='Load');
      const r=b.getBoundingClientRect(); return {x:Math.round(r.left+r.width/2),y:Math.round(r.top+r.height/2)}; })()`);
    for (const type of ['mousePressed','mouseReleased'])
      await page.send('Input.dispatchMouseEvent', { type, x: pt.x, y: pt.y, button: 'left', clickCount: 1 });
    const ch = await Promise.race([chooser, sleep(8000).then(() => null)]);
    if (!ch) throw new Error('no file chooser');
    await page.send('DOM.setFileInputFiles', { files: [process.env.FIXTURE], backendNodeId: ch.backendNodeId });
    await sleep(8000);
  } else {
    const setup = await ev(`(() => {
      const fire = el => { el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); };
      const set = (n,v) => { const el=document.querySelector('[name='+n+']'); if(!el) return 'missing '+n; el.value=String(v); fire(el); return 'ok'; };
      return { hours: set('totalhours', ${SIM}), tick: set('hourspertick', ${TICK}) };
    })()`);
    if (Object.values(setup).some(v => String(v).startsWith('missing'))) throw new Error('setup: ' + JSON.stringify(setup));

    const clicked = await ev(`(() => { const b=document.querySelector('button[title=Simulate]');
      if (!b) return 'no simulate button'; b.click(); return 'clicked'; })()`);
    if (clicked !== 'clicked') throw new Error(clicked);

    const deadline = Date.now() + 420000;
    while (Date.now() < deadline) {
      await sleep(2000);
      const st = await ev(`(() => { const b=document.querySelector('button[title=Simulate]');
        return { computing: b ? b.innerText.trim() !== '\\u{1F680}' : null,
                 frames: (document.querySelector('input[name=frameslider]')||{}).max }; })()`);
      if (st && st.computing === false && +st.frames > 1) break;
    }
    await sleep(3000);
  }

  //Lighting affects the per-step cost on the WebGL backend, which rebuilds its density field
  //whenever the plants change, so the tier has to be part of the measurement.
  if (process.env.TIER !== undefined && process.env.TIER !== '') {
    await ev(lightingScript(process.env.TIER, process.env.DAYLIGHT === '1', false, process.env.SKY === '1'));
    await sleep(2500);
  }

  // Step the playhead one frame at a time through the whole history, timing each step. The
  // step buttons drive the same playback FSM the play button does, so this measures the real
  // decode-plus-rebuild-plus-render path rather than a synthetic one.
  const result = await ev(`(async () => {
    const btn = t => [...document.querySelectorAll('button')].find(b => (b.title||'') === t);
    const slider = document.querySelector('input[name=frameslider]');
    const frames = slider ? +slider.max : 0;
    if (!frames) return { error: 'no history' };

    const seekStart = btn('Seek to start'), stepFwd = btn('Next frame'), stepBack = btn('Previous frame');
    if (!seekStart || !stepFwd || !stepBack) return { error: 'buttons: ' + [...document.querySelectorAll('button')].map(b=>b.title).join('|') };

    const raf = () => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
    const drain = () => new Promise(r => setTimeout(r, 0));

    //Three clocks, because they measure different things and only one of them moves when the
    //decoder changes:
    //  sync   - the click itself: the playback FSM, the base64 decode and readAgroScene.
    //  settle - plus the signal effects it schedules, i.e. the instance-list rebuild.
    //  step   - plus two animation frames, so it is pinned to the display cadence and mostly
    //           reports 2/60 s no matter what the CPU did.
    const sync = [], settle = [], step = [];

    for (let lap = 0; lap < ${LAPS}; ++lap) {
      seekStart.click(); await raf(); await raf();
      const button = lap % 2 === 0 ? stepFwd : stepBack;
      if (lap % 2 === 1) { for (let i = 0; i < frames; ++i) { stepFwd.click(); await raf(); } }
      for (let i = 0; i < frames; ++i) {
        const t0 = performance.now();
        button.click();
        const t1 = performance.now();
        await drain();
        const t2 = performance.now();
        await raf();
        sync.push(t1 - t0); settle.push(t2 - t0); step.push(performance.now() - t0);
      }
    }

    const stats = a => { const s = a.slice().sort((x, y) => x - y);
      const q = p => s[Math.min(s.length - 1, Math.floor(s.length * p))];
      return { median: +q(0.5).toFixed(3), p90: +q(0.9).toFixed(3), min: +s[0].toFixed(3) }; };

    return { frames, samples: sync.length, sync: stats(sync), settle: stats(settle), step: stats(step) };
  })()`, true);

  if (result.error) throw new Error(result.error);
  console.log(JSON.stringify({
    tier: process.env.TIER ?? null, frames: result.frames, samples: result.samples,
    syncMs: result.sync, settleMs: result.settle, stepMs: result.step,
    settleFps: +(1000 / result.settle.median).toFixed(1),
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception').map(l => l.text.split('\n')[0].slice(0, 120)),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
