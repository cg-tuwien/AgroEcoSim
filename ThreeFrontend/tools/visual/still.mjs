// Loads the fixture, triggers the offline still, and captures the produced PNG by
// intercepting the Blob the download helper creates.
import { launch, newPage, collectDiagnostics, lightingScript, sleep, shutdown } from './cdp.mjs';
import { writeFileSync } from 'node:fs';

const [url, sessionPath, outPath] = process.argv.slice(2);
const W = process.env.W || '640', H = process.env.H || '400', SSAA = process.env.SSAA || '2';

const browser = await launch(+(process.env.CDP_PORT || 9700));
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

  //Run a short simulation instead of loading the fixture. The saved session holds a mature
  //plant; a young one is a different scene, and some artifacts only appear on it.
  if (process.env.SIMULATE) {
    const setup = await ev(`(() => {
      const fire = el => { el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); };
      const set = (n,v) => { const el=document.querySelector('[name='+n+']'); if(!el) return 'missing '+n; el.value=String(v); fire(el); return 'ok'; };
      return { hours: set('totalhours', ${JSON.stringify(process.env.SIMULATE)}), tick: set('hourspertick', 4) };
    })()`);
    if (Object.values(setup).some(v => String(v).startsWith('missing'))) throw new Error('sim setup: ' + JSON.stringify(setup));
    await ev(`(() => { const b=Array.from(document.querySelectorAll('button')).find(x=>x.textContent.trim()==='Start'); if(!b) return 'missing'; b.click(); return 'clicked'; })()`);
    const deadline = Date.now() + 240000;
    while (Date.now() < deadline) {
      await sleep(4000);
      const busy = await ev(`(() => { const b=Array.from(document.querySelectorAll('button')).find(x=>x.textContent.trim()==='Start'); return b ? b.disabled : null; })()`);
      if (busy === false) break;
    }
    await sleep(3000);
  }

  // Loading the fixture is skipped for the empty-scene invariant check, which needs a scene
  // with no plants at all - hiding leaves and roots still leaves the stems behind.
  if (process.env.SKIPLOAD !== '1' && !process.env.SIMULATE) {
    // Load the fixture through the app's own Load button.
    const chooser = new Promise(r => page.on(m => { if (m.method === 'Page.fileChooserOpened') r(m.params); }));
    await page.send('Page.setInterceptFileChooserDialog', { enabled: true });
    const pt = await ev(`(() => { const b=Array.from(document.querySelectorAll('button')).find(x=>x.textContent.trim()==='Load'); const r=b.getBoundingClientRect(); return {x:Math.round(r.left+r.width/2),y:Math.round(r.top+r.height/2)}; })()`);
    for (const type of ['mousePressed','mouseReleased'])
      await page.send('Input.dispatchMouseEvent', { type, x: pt.x, y: pt.y, button: 'left', clickCount: 1 });
    const ch = await Promise.race([chooser, sleep(8000).then(()=>null)]);
    if (!ch) throw new Error('no file chooser');
    await page.send('DOM.setFileInputFiles', { files: [sessionPath], backendNodeId: ch.backendNodeId });
    await sleep(6000);
    await ev(`(() => { const b=document.querySelector('button[title="Seek to end"]'); if(b) b.click(); return 1; })()`);
    await sleep(3000);
  }

  //The empty-field invariant: with nothing injected, a GI tier must be byte-identical to the
  //tier below it. Terrain is excluded from the field by design, so hiding the plants and
  //seeds leaves the grid genuinely empty.
  if (process.env.NOPLANTS === '1')
    await ev(`(() => {
      const off = n => { const el = document.querySelector('[name=' + n + ']'); if (!el) return 'missing ' + n;
        el.checked = false;
        el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true}));
        return 'ok'; };
      return [off('showLeaves'), off('showRoots'), off('showSeeds')].join(',');
    })()`);
  if (process.env.NOPLANTS === '1') await sleep(2000);

  if (process.env.VOXELS === '1')
    await ev(`(() => { const el=document.getElementById('showVoxels'); if(!el) return 'missing';
      el.checked = true;
      el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true}));
      const gain = document.getElementById('voxelGain');
      if (gain) { gain.value = ${JSON.stringify(process.env.VOXEL_GAIN || '1')}; gain.dispatchEvent(new Event('input',{bubbles:true})); gain.dispatchEvent(new Event('change',{bubbles:true})); }
      return 'on'; })()`);
  await sleep(2500);

  // Configure and fire the still. The Export tab hosts the controls; a hidden tab panel is
  // still in the DOM and still receives dispatched events, so it need not be activated.
  const cfg = await ev(`(() => {
    const fire = el => { el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); };
    const set = (n,v) => { const el=document.querySelector('[name='+n+']'); if(!el) return 'missing '+n; el.value=String(v); fire(el); return 'ok'; };
    return { w: set('offlineWidth', ${W}), h: set('offlineHeight', ${H}), ssaa: set('offlineSsaa', ${SSAA}) };
  })()`);
  //Optional camera override, for close-ups. The saved pose frames the whole field, which is
  //useless for looking at a shadow on a single leaf. Needs ?debughook=1 on the URL.
  if (process.env.CAMERA) {
    const c = process.env.CAMERA.split(',').map(Number);
    const posed = await ev(`(() => {
      const r = window.__renderer;
      if (!r) return 'no debug hook';
      //Merge, do not replace: CameraPose also carries up, fovY, near and far, and a partial
      //object silently produces a degenerate projection that renders an empty frame.
      const pose = r.getCameraPose();
      pose.position = { x: ${c[0]}, y: ${c[1]}, z: ${c[2]} };
      pose.target = { x: ${c[3]}, y: ${c[4]}, z: ${c[5]} };
      r.setCameraPose(pose);
      return 'ok';
    })()`);
    if (posed !== 'ok') throw new Error('camera override failed: ' + posed);
    await sleep(1500);
  }

  //Lighting is four independent checkboxes; the harness still speaks in tier numbers.
  //TRANSLUCENCY alone still has to reach the checkboxes, so it counts as a request to set them
  //even with no TIER: TIER unset means tier 0, not "leave the UI alone", once anything else is
  //being asked for.
  const noTier = process.env.TIER === undefined || process.env.TIER === '';
  const tierSet = noTier && process.env.TRANSLUCENCY !== '1' && process.env.DAYLIGHT !== '1'
    ? 'default'
    : await ev(lightingScript(process.env.TIER, process.env.DAYLIGHT === '1', process.env.TRANSLUCENCY === '1'));
  cfg.tier = tierSet;
  await sleep(500);
  //What the renderer actually resolved, not just what the DOM says. A checkbox that was found
  //is not a signal that updated, and the two have disagreed before.
  cfg.resolved = await ev(`(() => {
    const r = window.__renderer;
    if (!r) return 'no hook';
    if (!r.lighting) return { kind: r.kind, note: 'no lighting field (three)' };
    const l = r.lighting;
    return { kind: r.kind, shadows: l.shadows, ao: l.ambientOcclusion, gi: l.voxelGI, bounces: l.bounces,
             sky: +(l.hemiSky ? l.hemiSky.g : -1).toFixed(3), sun: +(l.sunColor ? l.sunColor.g : -1).toFixed(3) };
  })()`);

  //Refuse to shoot a still whose lighting is not the lighting that was asked for.
  //
  //Setting the checkboxes and sleeping is not enough: a run that captured before the signals
  //propagated produced a tier-0 image labelled tier 1, and the resulting 25% "difference"
  //between two adjacent tiers looked exactly like a renderer regression. Reading back what the
  //renderer actually resolved turns that into a loud failure. Needs ?debughook=1 on the URL.
  if (process.env.TIER !== undefined && process.env.TIER !== '') {
    const t = Number(process.env.TIER) || 0;
    const want = { shadows: t >= 1, ao: t >= 2, gi: t >= 3 };
    const got = cfg.resolved;
    if (typeof got !== 'object' || got === null)
      throw new Error(`cannot verify TIER=${process.env.TIER}: ${got}. Add ?debughook=1 to the URL.`);
    if (got.kind === 'webgpu')
      for (const [k, v] of Object.entries(want)) {
        //Shadows are sun shadows, so with no sun above the horizon the renderer resolves them
        //off no matter what the checkbox says - which is what happens with SKIPLOAD=1, where
        //there is no simulated clock at all. That is correct behaviour, not a mis-set control.
        if (k === 'shadows' && !(got.sun > 0)) continue;
        if (got[k] !== v)
          throw new Error(`TIER=${process.env.TIER} wanted ${k}=${v} but the renderer resolved ${k}=${got[k]} (${JSON.stringify(got)})`);
      }
  }

  await ev(`(() => {
    window.__stillBlob = null;
    const orig = URL.createObjectURL.bind(URL);
    URL.createObjectURL = b => { window.__stillBlob = b; return orig(b); };
    return 1;
  })()`);

  await ev(`(() => { const b=document.querySelector('button[name=renderStill]'); if(!b) return 'missing'; b.click(); return 'clicked'; })()`);
  //8 s is plenty for one plant and nowhere near enough for a production scene: 1.26M
  //primitives through the software rasterizer takes minutes, and a short wait just yields
  //bytes:0 with no error, which reads as "the export is broken" rather than "not finished".
  const stillWaitMs = +(process.env.STILL_WAIT || 8000);
  for (let waited = 0; waited < stillWaitMs; waited += 2000) {
    await sleep(2000);
    if (await ev(`!!window.__stillBlob`)) break;
  }

  const b64 = await ev(`(async () => {
    if (!window.__stillBlob) return null;
    const buf = await window.__stillBlob.arrayBuffer();
    let s = ''; const v = new Uint8Array(buf);
    for (let i = 0; i < v.length; ++i) s += String.fromCharCode(v[i]);
    return btoa(s);
  })()`, true);

  if (b64) writeFileSync(outPath, Buffer.from(b64, 'base64'));
  console.log(JSON.stringify({ cfg, gotBlob: !!b64, bytes: b64 ? Buffer.from(b64,'base64').length : 0,
    errors: logs.filter(l=>l.level==='error'||l.level==='exception').map(l=>l.text.split('\n')[0].slice(0,150)),
    all: process.env.ALLLOGS === '1'
      ? logs.filter(l => !/GL Driver|external Instance/.test(l.text)).map(l => l.level + ': ' + l.text.split('\n')[0].slice(0,200))
      : undefined }, null, 1));
} finally { page?.close(); await shutdown(browser); }
