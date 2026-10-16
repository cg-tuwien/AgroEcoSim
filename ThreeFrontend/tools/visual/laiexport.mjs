// Is the leaf-area-index document complete, or only totals?
//
// The per-obstacle weekly series ARE dropped on purpose above the wire budget, and the selection
// series and every total are meant to survive regardless - so the question is not whether the
// arrays are null but whether the document still carries the seasonal curve, whether it SAYS why
// the others are gone, and whether the complete document is retrievable from the run store. All
// three are checked here, on a run small enough to stay under the budget and again on one large
// enough to exceed it (OBSTACLES=... sets how many walls; the budget is 20,000 weekly records).
//
// It also checks the vegetation survey: the plain field is one tray named "field" whose leaf area
// must be there and non-zero by the end of a 30-day summer run.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';
import { writeFileSync } from 'node:fs';

const [url, backend, outPath] = process.argv.slice(2);
const OBSTACLES = +(process.env.OBSTACLES || 3);
const browser = await launch(+(process.env.CDP_PORT || 25200));
let page;

const ev = async (p, e, awaitPromise = false) => {
  const r = await p.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise, timeout: 900000 });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  return r.result.value;
};

try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable'); await page.send('Page.enable');
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(20000)]);
  for (let i = 0; i < 60; ++i) { if (await ev(page, `!!window.__appstate`)) break; await sleep(1000); }
  await sleep(1500);

  // Walls to measure, plants in front of them, and the filter that selects them. Render mode 3 -
  // the internal tracer - because modes 1 and 2 light plants but measure nothing on a facade
  // unless the solver runs beside them, and mode 0 is flat ambient.
  const setup = await ev(page, `(() => { const a = window.__appstate;
      a.totalHours.value = ${process.env.TOTAL_HOURS || 720}; a.hoursPerTick.value = 4;
      a.randomize.value = false; a.initNumber.value = 42; a.renderMode.value = 3;
      a.initialDate.value = '${process.env.INITIAL_DATE || '2026-05-01'}';
      a.leafAreaIndexObstacles.value = 'wall';

      //There is no obstacle to clone a constructor from on a fresh page, so make one through the
      //app's own helper and reuse its class - a random obstacle placed and then re-typed is still
      //the app's construction path, which is the point.
      a.pushRndObstacle();
      const Obstacle = a.obstacles.value[0].constructor;
      const walls = [];
      for (let i = 0; i < ${OBSTACLES}; ++i)
        walls.push(new Obstacle('wall', 1 + i * 2.2, 0, 5, 0, 0, 2, 2.5, 0.2, new Float32Array(0), []));
      a.obstacles.value = walls;
      //The filter matches on the obstacle NAME the backend sees. A hand-placed wall is named by
      //the request builder, so check what the request will actually carry rather than assuming.
      const req = a.requestBody();

      //On the +Z side of the walls: that is the face the meter calls outer for a free-standing
      //slab (its reverse is "enclosed" by construction), so leaves here are the ones its leaf
      //area index can see. Seeds at 4.6 sat behind the enclosed face and read exactly zero.
      const Seed = a.seeds.value[0].constructor, species = a.species.value[0].name.peek();
      const seeds = [];
      for (let i = 0; i < ${OBSTACLES}; ++i)
        for (let k = 0; k < 4; ++k)
          seeds.push(new Seed(species, 1 + i * 2.2 + k * 0.4, -0.01, 5.35, 0, false));
      a.seeds.value = seeds;
      return { obstacles: a.obstacles.value.length, seeds: a.seeds.value.length,
               filter: a.leafAreaIndexObstacles.value,
               requestObstacles: (req.Obstacles || []).length,
               requestFilter: req.LeafAreaIndexObstacles }; })()`);

  await ev(page, `(() => { document.querySelector('button[title=Simulate]').click(); return 1; })()`);
  let runId = '';
  for (let i = 0; i < 120; ++i) { await sleep(500); runId = await ev(page, `window.__appstate.runId.value`); if (runId) break; }
  if (!runId) throw new Error('no run id announced');
  for (let i = 0; i < 900; ++i) {
    await sleep(2000);
    const st = await ev(page, `(async () => { const r = await fetch('${backend}/Simulation/run/${runId}');
        return r.ok ? (await r.json()).status : 'gone'; })()`, true);
    if (st !== 'running') break;
  }
  // The document rides on the Result message, which lands after the status flips.
  for (let i = 0; i < 30; ++i) { await sleep(1000); if (await ev(page, `!!window.__appstate.obstacleLight.value`)) break; }

  // The document exactly as the socket delivered it: what the HUD shows and what the export
  // writes when it is complete.
  const d = await ev(page, `(() => { const a = window.__appstate; const d = a.obstacleLight.value;
      return d ? d : { missing: 'obstacleLight is ' + (d === null ? 'null' : 'undefined') }; })()`);
  if (d.missing) throw new Error(`nothing to export: ${d.missing}`);
  if (outPath) writeFileSync(outPath, JSON.stringify(d, undefined, 1));

  const weeks = d.periods.weeks.length;
  const obstacles = d.obstacles || [];
  const aligned = xs => Array.isArray(xs) && xs.length === weeks;
  const withWeeks = obstacles.filter(o => aligned(o.weeks)).length;
  const surfaces = obstacles.flatMap(o => o.surfaces || []);
  const facadeSides = surfaces.filter(s => s.side !== 'edge');
  const facadeSidesWithWeeks = facadeSides.filter(s => aligned(s.weeks)).length;
  const edgesWithoutWeeks = surfaces.filter(s => s.side === 'edge' && s.weeks === null).length;
  const selWeeks = d.selection ? d.selection.weeks.filter(w => w).length : -1;
  const sample = selWeeks > 0 ? d.selection.weeks.find(w => w) : null;
  // A record is only useful if it carries numbers. A record of zeroes is the same emptiness
  // wearing a different shape, and that is exactly the failure this is looking for.
  const nonZero = sample ? Object.entries(sample).filter(([k, v]) => typeof v === 'number' && v !== 0).length : 0;
  const tray = (d.trays || [])[0];
  const trayLeafArea = tray && tray.total ? tray.total.leafArea_m2 : null;

  // And the file the server keeps: it must exist, decode, be complete, and carry the series the
  // socket copy may be missing. A URL that 404s is worse than no URL at all.
  const full = await ev(page, `(async () => {
      const r = await fetch('${backend}/Simulation/run/${runId}/obstaclelight');
      if (!r.ok) return { httpStatus: r.status };
      //The browser inflates Content-Encoding: gzip on the way in, so this is plain JSON.
      const text = await r.text();
      const parsed = JSON.parse(text);
      const obs = parsed.obstacles || [];
      const weeks = parsed.periods.weeks.length;
      return { httpStatus: r.status, bytesReceived: text.length, format: parsed.format, complete: parsed.complete,
               omitted: parsed.omitted, obstacles: obs.length,
               obstaclesWithWeeks: obs.filter(o => Array.isArray(o.weeks) && o.weeks.length === weeks).length,
               weeks, trays: (parsed.trays || []).length, runId: parsed.runId,
               hasDefinitions: !!parsed.definitions && Object.keys(parsed.definitions).length > 10 }; })()`, true);

  console.log(JSON.stringify({
    setup,
    matched: d.meta ? { available: d.meta.obstaclesAvailable, matched: d.meta.obstaclesMatched,
                        sensors: d.meta.sensors, renderer: d.meta.renderer, filterMode: d.meta.filterMode,
                        coverage: d.meta.coverage } : null,
    format: d.format, complete: d.complete, omitted: d.omitted,
    weeks, obstacles: obstacles.length, obstaclesWithWeeks: withWeeks,
    facadeSides: facadeSides.length, facadeSidesWithWeeks, edges: surfaces.length - facadeSides.length, edgesWithoutWeeks,
    selectionWeeks: selWeeks, selectionSampleNonZeroFields: nonZero, selectionSample: sample,
    trays: (d.trays || []).length, trayName: tray ? tray.name : null, trayLeafArea_m2: trayLeafArea,
    selectionLeafAreaIndexMax: d.selection ? d.selection.total.leafAreaIndexMax : null,
    plantedFaceLeafAreaIndexMax: obstacles.length && obstacles[0].surfaces.length ? obstacles[0].surfaces[0].total.leafAreaIndexMax : null,
    storedDocument: full,
    // The whole point: under the budget every level carries weeks; over it the selection series
    // must still be there, the flag must say so, and the stored file must be complete either way.
    verdict: selWeeks <= 0 ? 'SELECTION SERIES MISSING - the one that must always survive'
      : !(full && full.httpStatus === 200 && full.complete === true && full.obstaclesWithWeeks === full.obstacles && full.obstacles > 0 && full.hasDefinitions)
        ? 'STORED DOCUMENT MISSING OR INCOMPLETE'
      : !(tray && trayLeafArea > 0) ? 'TRAY SURVEY MISSING OR EMPTY'
      : d.complete === false && withWeeks === 0 && d.omitted.length > 0
        ? 'BUDGET APPLIED, selection series intact, complete document stored'
      : d.complete === true && withWeeks === obstacles.length && facadeSidesWithWeeks === facadeSides.length
        && edgesWithoutWeeks === surfaces.length - facadeSides.length
        ? 'COMPLETE - every level carries weeks'
      : d.complete === true && withWeeks < obstacles.length ? 'INCONSISTENT - series dropped but flag says complete'
      : 'PARTIAL',
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0, 200)).filter(t => !/favicon/.test(t)).slice(0, 4),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
