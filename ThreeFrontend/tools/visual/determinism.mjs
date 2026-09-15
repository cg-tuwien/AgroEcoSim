// Runs the same request twice and compares the stored frames byte for byte.
//
// The repo documents "two identical requests produce different plants (188 vs 167 organs)".
// Now that every frame is stored server-side, that claim can be measured instead of recalled -
// and the same check tells us whether a fix worked.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';

const [url, backend] = process.argv.slice(2);
const SEED = process.env.SEED || '424242';
const browser = await launch(+(process.env.CDP_PORT || 24600));
let page;

async function runOnce(page, ev) {
  //NOTE: this whole string is a template literal. A backtick anywhere inside it - even in a
  //comment - terminates it early, which is what broke this file before. Keep them out.
  //Set the signals, not the inputs. The HoursPerTick control snaps its value against the CURRENT
  //one, so driving it through the DOM is stateful: the first run came out at 3 h/tick and the
  //second at 4, and comparing those two proves nothing at all.
  await ev(`(() => { const a = window.__appstate;
      a.totalHours.value = ${process.env.TOTAL_HOURS || 240}; a.hoursPerTick.value = 4;
      a.randomize.value = false; a.initNumber.value = ${SEED};
      a.deterministic.value = ${process.env.DETERMINISTIC === '1'};
      //Plant count matters more than anything else here. SimulationWorld only takes the parallel
      //path when Formations.Count > 1, and it is the parallel post delivery that is suspected of
      //making runs diverge - a single plant would never exercise it.
      //Placed explicitly, NOT via pushSeedRaster: that helper picks a random species per
      //position - species[Math.floor(Math.random()*species.length)] - so two runs seeded with it
      //are not the same request and comparing them measures the helper, not the simulation.
      const n = ${process.env.PLANTS || 0};
      if (n > 0) {
        const Seed = a.seeds.value.length ? a.seeds.value[0].constructor : null;
        if (!Seed) return { error: 'no seed to clone a constructor from' };
        const species = a.species.value[1] ? a.species.value[1].name.peek() : a.species.value[0].name.peek();
        const side = Math.ceil(Math.sqrt(n));
        const out = [];
        for (let i = 0; i < n; ++i) {
          const x = 0.5 + (i % side) * (9 / side);
          const z = 0.5 + Math.floor(i / side) * (9 / side);
          out.push(new Seed(species, x, 0, z, 0, false));
        }
        a.seeds.value = out;
      }
      return { seeds: a.seeds.value.length }; })()`);

  //The Simulate button is also Stop while a run is in flight, so clicking it at the wrong moment
  //aborts instead of starting. Wait for idle, then watch for a NEW run id rather than assuming
  //the click did what was wanted - that assumption produced a "measurement" of two runs with
  //different step counts, which is not two runs of the same request at all.
  const idle = async () => (await ev(`(() => { const b=document.querySelector('button[title=Simulate]');
    return b ? b.innerText.trim() === '\\u{1F680}' : true; })()`));
  for (let i = 0; i < 120 && !(await idle()); ++i) await sleep(1000);
  if (!await idle()) throw new Error('previous run never went idle');

  const before = await ev(`window.__appstate.runId.value`);
  await ev(`(() => { document.querySelector('button[title=Simulate]').click(); return 1; })()`);

  let runId = '';
  for (let i = 0; i < 60; ++i) {
    await sleep(500);
    const now = await ev(`window.__appstate.runId.value`);
    if (now && now !== before) { runId = now; break; }
  }
  if (!runId) throw new Error('a new run never started - the click probably hit Stop');

  for (let i = 0; i < 300; ++i) {
    await sleep(2000);
    if (await idle()) return runId;
  }
  throw new Error('run did not finish');
}

try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable'); await page.send('Page.enable');
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(15000)]);
  await sleep(2500);
  const ev = async (e, awaitPromise = false) => {
    const r = await page.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise, timeout: 600000 });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };
  for (let i = 0; i < 60; ++i) { if (await ev(`!!window.__appstate`)) break; await sleep(1000); }

  //Three runs, not two. If a process-global is mutated by the FIRST run - SpeciesSettings.
  //Predefined is a static that Parameters.Init mutates once, guarded by an Initialized flag -
  //then run 1 differs from run 2 while runs 2 and 3 agree. Two runs cannot tell that apart from
  //genuine per-run randomness; three can.
  const runA = await runOnce(page, ev);
  await sleep(2000);
  const runB = await runOnce(page, ev);
  await sleep(2000);
  const runC = await runOnce(page, ev);

  // Compare the two stored runs frame by frame, entirely server-side data.
  const compare = async (x, y) => ev(`(async () => {
      const get = async (id, step) => {
        const r = await fetch('${backend}/Simulation/run/' + id + '/frame/' + step);
        return r.ok ? new Uint8Array(await r.arrayBuffer()) : null;
      };
      const meta = async id => (await fetch('${backend}/Simulation/run/' + id)).json();
      const a = await meta('${x}'), b = await meta('${y}');

      //A frame-by-frame diff of two DIFFERENT requests is meaningless. Prove they match first.
      if (a.hoursPerTick !== b.hoursPerTick || a.totalSteps !== b.totalSteps)
        return { sameRequest: false, a: {h: a.hoursPerTick, n: a.totalSteps}, b: {h: b.hoursPerTick, n: b.totalSteps} };

      const n = Math.min(a.storedFrames, b.storedFrames);
      let firstDiff = -1, sizeDiffs = 0;
      for (let i = 0; i < n; ++i) {
        const fa = await get('${x}', i), fb = await get('${y}', i);
        if (!fa || !fb) continue;
        if (fa.length !== fb.length) { sizeDiffs++; if (firstDiff < 0) firstDiff = i; continue; }
        for (let k = 0; k < fa.length; ++k)
          if (fa[k] !== fb[k]) { if (firstDiff < 0) firstDiff = i; k = fa.length; sizeDiffs++; }
      }
      return { sameRequest: true, hoursPerTick: a.hoursPerTick,
               framesA: a.storedFrames, framesB: b.storedFrames, compared: n,
               firstDifferingStep: firstDiff, differingFrames: sizeDiffs }; })()`, true);

  const ab = await compare(runA, runB);
  const bc = await compare(runB, runC);

  const same = c => c.sameRequest && c.differingFrames === 0 && c.framesA === c.framesB;
  console.log(JSON.stringify({ seed: SEED, deterministic: process.env.DETERMINISTIC === '1',
    runs: [runA.slice(0,8), runB.slice(0,8), runC.slice(0,8)],
    'A_vs_B': { identical: same(ab), firstDifferingStep: ab.firstDifferingStep, differingFrames: ab.differingFrames },
    'B_vs_C': { identical: same(bc), firstDifferingStep: bc.firstDifferingStep, differingFrames: bc.differingFrames },
    verdict: same(ab) && same(bc) ? 'deterministic'
           : !same(ab) && same(bc) ? 'FIRST RUN DIFFERS - a process-global is mutated once'
           : 'non-deterministic every run',
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0,120)).filter(t => !/CONNECTION_REFUSED/.test(t)).slice(0,2) }, null, 1));
} finally { page?.close(); await shutdown(browser); }
