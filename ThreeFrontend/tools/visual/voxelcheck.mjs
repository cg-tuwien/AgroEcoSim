// Analytic gate for the voxel field (plan phase 14).
//
// Turns the density overlay on, which forces a field build, then compares the GPU's
// per-cell leaf area against the CPU reference run over the same buffers. The unit tests
// cover the algorithm; this covers the WGSL port of it, which they cannot reach.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';

const [url, sessionPath] = process.argv.slice(2);

const browser = await launch(+(process.env.CDP_PORT || 9760));
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

  const chooser = new Promise(r => page.on(m => { if (m.method === 'Page.fileChooserOpened') r(m.params); }));
  await page.send('Page.setInterceptFileChooserDialog', { enabled: true });
  const pt = await ev(`(() => {
    const b = Array.from(document.querySelectorAll('button')).find(x => x.textContent.trim() === 'Load');
    if (!b) return { buttons: Array.from(document.querySelectorAll('button')).map(x => x.textContent.trim()).slice(0, 40) };
    const r = b.getBoundingClientRect();
    return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) };
  })()`);
  if (pt.buttons) throw new Error('no Load button; saw: ' + JSON.stringify(pt.buttons));
  for (const type of ['mousePressed','mouseReleased'])
    await page.send('Input.dispatchMouseEvent', { type, x: pt.x, y: pt.y, button: 'left', clickCount: 1 });
  const ch = await Promise.race([chooser, sleep(8000).then(()=>null)]);
  if (!ch) throw new Error('no file chooser');
  await page.send('DOM.setFileInputFiles', { files: [sessionPath], backendNodeId: ch.backendNodeId });
  await sleep(6000);
  await ev(`(() => { const b=document.querySelector('button[title="Seek to end"]'); if(b) b.click(); return 1; })()`);
  await sleep(3000);

  // Ticking the checkbox is what builds the field; nothing allocates it until then.
  // A real click, not a synthesised event: preact wires this control through its own
  // listener and a dispatched Event does not always reach it.
  const box = await ev(`(() => {
    const el = document.getElementById('showVoxels');
    if (!el) return { error: 'missing' };
    el.scrollIntoView({ block: 'center' });
    const r = el.getBoundingClientRect();
    return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2), w: r.width, h: r.height, checked: el.checked };
  })()`);
  await ev(`(() => { const el=document.getElementById('showVoxels'); el.checked=true;
    el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); return 1; })()`);
  await sleep(4000);
  const shown = await ev(`!!document.getElementById('voxelMip')`);

  const result = await ev(`(async () => {
    const r = window.__renderer;
    if (!r) return { error: 'no renderer handle' };
    if (!r.readVoxelArea) return { error: 'backend has no voxel field', kind: r.kind };
    const occupancy = r.readVoxelOccupancy ? await r.readVoxelOccupancy() : undefined;
    const gpu = await r.readVoxelArea();
    const cpu = r.debugVoxelReference();
    if (!gpu || !cpu) return { error: 'field not built', kind: r.kind, hasGpu: !!gpu, hasCpu: !!cpu,
                               checkbox: document.getElementById('showVoxels')?.checked,
                               showVoxels: r.showVoxels, dirty: r.voxelsDirty, giInputs: r.giInputs,
                               opts: r.voxelOptions, hasSetter: typeof r.setVoxelDebug,
                               hasField: !!r.voxels, hasAtlas: !!r.atlas,
                               keys: Object.keys(r).filter(k => /vox|Vox/i.test(k)) };

    let gpuTotal = 0, cpuTotal = 0, occupiedGpu = 0, occupiedCpu = 0;
    let worst = 0, worstAt = -1, bothOccupied = 0;
    for (let i = 0; i < gpu.length; ++i) {
      gpuTotal += gpu[i]; cpuTotal += cpu[i];
      if (gpu[i] > 0) ++occupiedGpu;
      if (cpu[i] > 0) ++occupiedCpu;
      if (gpu[i] > 0 && cpu[i] > 0) ++bothOccupied;
      const d = Math.abs(gpu[i] - cpu[i]);
      if (d > worst) { worst = d; worstAt = i; }
    }
    // What is actually in the field, by world position, so the overlay can be checked
    // against the geometry rather than eyeballed.
    const grid = r.voxels.grid;
    const top = [];
    for (let i = 0; i < gpu.length; ++i) if (gpu[i] > 0) top.push([i, gpu[i]]);
    top.sort((a,b) => b[1]-a[1]);
    const describe = ([i, a]) => {
      const d = grid.dim;
      const x = i % d, y = Math.floor(i / d) % d, z = Math.floor(i / (d*d));
      return { area: +a.toFixed(4),
               wx: +(grid.origin.x + (x+0.5)*grid.cell.x).toFixed(2),
               wy: +(grid.origin.y + (y+0.5)*grid.cell.y).toFixed(2),
               wz: +(grid.origin.z + (z+0.5)*grid.cell.z).toFixed(2) };
    };
    return {
      kind: r.kind, cells: gpu.length, compacted: occupancy,
      cell: +grid.cell.x.toFixed(4),
      topCells: top.slice(0, 8).map(describe),
      aboveSoil: top.filter(([i]) => grid.origin.y + (Math.floor(i/grid.dim)%grid.dim + 0.5)*grid.cell.y > 0.02).length,
      gpuTotal, cpuTotal,
      relError: cpuTotal > 0 ? Math.abs(gpuTotal - cpuTotal) / cpuTotal : null,
      occupiedGpu, occupiedCpu, bothOccupied,
      worstCellDelta: worst, worstAt,
    };
  })()`, true);

  console.log(JSON.stringify({ shown, result,
    errors: logs.filter(l=>l.level==='error'||l.level==='exception').map(l=>l.text.split('\n')[0].slice(0,180)) }, null, 1));
} finally { page?.close(); await shutdown(browser); }
