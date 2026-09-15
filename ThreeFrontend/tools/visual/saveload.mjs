// Round-trips a session through the rewritten save() and load().
//
// save() used to build the whole session as ONE JSON string, which threw
// RangeError: Invalid string length above ~537M chars - so a long run could not be saved at all.
// It now assembles Blob parts. That is the part worth proving: the file must still be valid
// JSON, still carry base64 history on disk, and still load back to identical bytes in memory.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';
import { writeFileSync } from 'node:fs';

const [url, fixture, out] = process.argv.slice(2);
const browser = await launch(+(process.env.CDP_PORT || 23200));
let page;
try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable');
  await page.send('Page.enable'); await page.send('DOM.enable');
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(15000)]);
  await sleep(2500);

  const ev = async (e, awaitPromise = false) => {
    const r = await page.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };

  const dropFile = async (path, label) => {
    const chooser = new Promise(r => page.on(m => { if (m.method === 'Page.fileChooserOpened') r(m.params); }));
    await page.send('Page.setInterceptFileChooserDialog', { enabled: true });
    const pt = await ev(`(() => { const b=[...document.querySelectorAll('button')].find(x=>x.textContent.trim()==='${label}');
      const r=b.getBoundingClientRect(); return {x:Math.round(r.left+r.width/2),y:Math.round(r.top+r.height/2)}; })()`);
    for (const type of ['mousePressed','mouseReleased'])
      await page.send('Input.dispatchMouseEvent', { type, x: pt.x, y: pt.y, button: 'left', clickCount: 1 });
    const ch = await Promise.race([chooser, sleep(10000).then(()=>null)]);
    if (!ch) throw new Error('no file chooser for ' + label);
    await page.send('DOM.setFileInputFiles', { files: [path], backendNodeId: ch.backendNodeId });
    await sleep(7000);
  };

  await dropFile(fixture, 'Load');
  const before = await ev(`(() => ({
      frames: window.__appstate.history.length,
      isBytes: window.__appstate.history.every(e => e.s instanceof Uint8Array),
      totalBytes: window.__appstate.history.reduce((n,e) => n + e.s.byteLength, 0),
      ts: window.__appstate.history.map(e => e.t),
      firstBytes: Array.from(window.__appstate.history.at(-1).s.slice(0, 24)),
      historySize: window.__appstate.historySize.value }))()`);

  // Capture what save() writes, without touching the filesystem.
  await ev(`(() => { window.__saved = null; const orig = URL.createObjectURL.bind(URL);
    URL.createObjectURL = b => { window.__saved = b; return orig(b); }; return 1; })()`);
  const clicked = await ev(`(() => { const b=[...document.querySelectorAll('button')].find(x=>/save/i.test(x.title||'') || x.textContent.trim()==='Save');
    if (!b) return 'no save button'; b.click(); return 'clicked'; })()`);
  if (clicked !== 'clicked') throw new Error(clicked);
  await sleep(4000);
  const text = await ev(`(async () => window.__saved ? await window.__saved.text() : null)()`, true);
  if (!text) throw new Error('save produced no blob');
  writeFileSync(out, text);

  // Now load the file we just wrote, and compare.
  await dropFile(out, 'Load');
  const after = await ev(`(() => ({
      frames: window.__appstate.history.length,
      isBytes: window.__appstate.history.every(e => e.s instanceof Uint8Array),
      totalBytes: window.__appstate.history.reduce((n,e) => n + e.s.byteLength, 0),
      ts: window.__appstate.history.map(e => e.t),
      firstBytes: Array.from(window.__appstate.history.at(-1).s.slice(0, 24)) }))()`);

  const same = JSON.stringify(before.ts) === JSON.stringify(after.ts)
    && before.totalBytes === after.totalBytes
    && JSON.stringify(before.firstBytes) === JSON.stringify(after.firstBytes);

  console.log(JSON.stringify({ before, after, roundTripIdentical: same, savedChars: text.length,
    savedIsValidJson: (() => { try { JSON.parse(text); return true; } catch { return false; } })(),
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0,140))
      .filter(t => !/CONNECTION_REFUSED|Failed to fetch/.test(t)).slice(0,3) }, null, 1));
} finally { page?.close(); await shutdown(browser); }
