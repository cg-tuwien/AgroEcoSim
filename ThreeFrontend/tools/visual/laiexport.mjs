// Is the leaf-area-index export complete, or only summaries?
//
// Reported as "empty days: [] all over the place". The per-obstacle daily series ARE dropped on
// purpose above `DailyRecordBudget`, and the selection-wide series is meant to survive regardless -
// so the question is not whether the arrays are empty but whether the file still carries the
// seasonal curve, and whether it SAYS why the others are gone. Both are checked here, on a run
// small enough to stay under the budget and again on one large enough to exceed it.
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

      const Seed = a.seeds.value[0].constructor, species = a.species.value[0].name.peek();
      const seeds = [];
      for (let i = 0; i < ${OBSTACLES}; ++i)
        for (let k = 0; k < 4; ++k)
          seeds.push(new Seed(species, 1 + i * 2.2 + k * 0.4, -0.01, 4.6, 0, false));
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
  // The measurement rides on the Result message, which lands after the status flips.
  for (let i = 0; i < 30; ++i) { await sleep(1000); if (await ev(page, `!!window.__appstate.obstacleLightMeta.value`)) break; }

  // Build the export document in the page, exactly as the download does.
  const doc = await ev(page, `(() => { const a = window.__appstate;
      const light = a.obstacleLight.value, meta = a.obstacleLightMeta.value, sel = a.obstacleLightSelection.value;
      if (!light) return { missing: 'obstacleLight is ' + (light === null ? 'null' : 'undefined') };
      //Built in Node from the run id, not spliced together here: a regex inside a template
      //literal inside an evaluated string is three levels of escaping and it broke the first time.
      const runId = a.runId.value;
      const url = meta && meta.dailySeriesOmitted && runId
        ? '${backend}' + '/Simulation/run/' + runId + '/obstaclelight'
        : undefined;
      return { built: window.__buildObstacleLightExport(meta, light, sel, url), url }; })()`);
  if (doc.missing) throw new Error(`nothing to export: ${doc.missing}`);
  const d = doc.built;
  if (outPath) writeFileSync(outPath, JSON.stringify(d, undefined, 1));

  const obstacles = d.obstacles || [];
  const withDays = obstacles.filter(o => (o.days || []).length > 0).length;
  const surfaces = obstacles.flatMap(o => o.surfaces || []);
  const surfacesWithDays = surfaces.filter(s => (s.days || []).length > 0).length;
  const selDays = d.selection ? (d.selection.days || []).length : -1;
  const omitted = d.meta ? d.meta.dailySeriesOmitted : null;

  // A day record is only useful if it carries numbers. An array of zeroes is the same emptiness
  // wearing a different shape, and that is exactly the failure this is looking for.
  const sample = selDays > 0 ? d.selection.days[Math.floor(selDays / 2)] : null;
  const nonZero = sample ? Object.entries(sample).filter(([k, v]) => typeof v === 'number' && v !== 0).length : 0;

  //And the file the server keeps when the wire copy had to be truncated: it must exist, decode,
  //and actually carry the series this one is missing. A URL written into an exported file that
  //404s is worse than no URL at all.
  let full = null;
  if (doc.url) {
    full = await ev(page, `(async () => {
        const r = await fetch(${JSON.stringify(doc.url)});
        if (!r.ok) return { httpStatus: r.status };
        //The browser inflates Content-Encoding: gzip on the way in, so this is plain JSON.
        const text = await r.text();
        const parsed = JSON.parse(text);
        const obs = parsed.obstacles || [];
        return { httpStatus: r.status, bytesReceived: text.length,
                 obstacles: obs.length,
                 obstaclesWithDays: obs.filter(o => (o.days || []).length > 0).length,
                 daysPerObstacle: obs.length ? (obs[0].days || []).length : 0,
                 surfacesWithDays: obs.flatMap(o => o.surfaces || []).filter(x => (x.days || []).length > 0).length,
                 runId: parsed.runId, hasMeta: !!parsed.meta, hasSelection: !!parsed.selection }; })()`, true);
  }

  console.log(JSON.stringify({
    setup,
    matched: d.meta ? { available: d.meta.obstaclesAvailable, matched: d.meta.obstaclesMatched,
                        sensors: d.meta.sensors, renderer: d.meta.renderer } : null,
    dailySeriesOmitted: omitted,
    obstacles: obstacles.length,
    obstaclesWithDays: withDays,
    surfaces: surfaces.length,
    surfacesWithDays,
    selectionDays: selDays,
    selectionSampleNonZeroFields: nonZero,
    selectionSample: sample,
    fullSeriesUrl: doc.url || null,
    fullSeriesFile: full,
    // The whole point: with the budget NOT exceeded every level should carry days; with it
    // exceeded the selection series must still be there and the flag must say so.
    verdict: selDays <= 0 ? 'SELECTION SERIES MISSING - the one that must always survive'
      //A truncated file is only acceptable if the detail is genuinely retrievable.
      : omitted === true && withDays === 0 && full && full.obstaclesWithDays === full.obstacles && full.obstacles > 0
        ? 'BUDGET APPLIED, selection series intact, full series retrievable'
      : omitted === true && withDays === 0 && doc.url
        ? 'TRUNCATED AND THE FULL SERIES IS NOT RETRIEVABLE'
      : omitted === true && withDays === 0 ? 'BUDGET APPLIED, selection series intact'
      : omitted === false && withDays === obstacles.length ? 'COMPLETE - every level carries days'
      : omitted === false && withDays === 0 ? 'INCONSISTENT - days dropped but flag says otherwise'
      : 'PARTIAL',
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0, 200)).filter(t => !/favicon/.test(t)).slice(0, 4),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
