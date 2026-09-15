// Kills the WebSocket mid-run and checks the run survives it.
//
// The loop used to live inside the Run hub invocation, so a dropped socket orphaned it: the
// simulation kept ticking with nowhere to send frames, and its connection id stayed marked
// "already running" forever. It belongs to the runner now, keyed by run id, and a reconnecting
// client rejoins the run's group.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';

const [url] = process.argv.slice(2);
const browser = await launch(+(process.env.CDP_PORT || 24200));
let page;
try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable'); await page.send('Page.enable');
  const loaded = new Promise(r => page.on(m => { if (m.method === 'Page.loadEventFired') r(); }));
  await page.send('Page.navigate', { url });
  await Promise.race([loaded, sleep(15000)]);
  await sleep(2500);

  const ev = async (e, awaitPromise = false) => {
    const r = await page.send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise, timeout: 300000 });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };
  for (let i = 0; i < 60; ++i) { if (await ev(`!!window.__appstate`)) break; await sleep(1000); }

  const set = (n, v) => ev(`(() => { const el=document.querySelector('[name=${n}]'); el.value=String(${JSON.stringify(v)});
    el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); return 1; })()`);
  await set('totalhours', 960);
  await set('hourspertick', 4);
  await ev(`(() => { document.querySelector('button[title=Simulate]').click(); return 1; })()`);

  // Wait until the run is properly under way.
  let runId = '';
  for (let i = 0; i < 60; ++i) {
    await sleep(1000);
    const s = await ev(`(() => ({ runId: window.__appstate.runId.value, frames: window.__appstate.history.length }))()`);
    if (s.runId && s.frames > 5) { runId = s.runId; break; }
  }
  if (!runId) throw new Error('run never started');
  const before = await ev(`window.__appstate.history.length`);

  // Drop the socket the way a flaky network would. SignalR's automatic reconnect takes over.
  await page.send('Network.enable');
  await page.send('Network.emulateNetworkConditions', { offline: true, latency: 0, downloadThroughput: 0, uploadThroughput: 0 });
  await sleep(4000);
  await page.send('Network.emulateNetworkConditions', { offline: false, latency: 0, downloadThroughput: -1, uploadThroughput: -1 });

  // Give it time to reconnect, resubscribe and receive more frames.
  let after = before, reconnected = false;
  for (let i = 0; i < 60; ++i) {
    await sleep(2000);
    const s = await ev(`(() => ({ frames: window.__appstate.history.length, runId: window.__appstate.runId.value }))()`);
    after = s.frames;
    if (after > before) { reconnected = true; break; }
  }

  const server = await ev(`(async () => {
      const r = await fetch('http://localhost:7215/Simulation/run/' + window.__appstate.runId.value);
      return r.ok ? await r.json() : null; })()`, true);

  console.log(JSON.stringify({
    runId: runId.slice(0, 12), framesBeforeDrop: before, framesAfterReconnect: after,
    keptStreaming: reconnected,
    serverStoredFrames: server ? server.storedFrames : null,
    serverStatus: server ? server.status : null,
    errors: logs.filter(l => l.level === 'error' || l.level === 'exception')
      .map(l => l.text.split('\n')[0].slice(0,120))
      .filter(t => !/CONNECTION_REFUSED|Failed to fetch|WebSocket/.test(t)).slice(0,3),
  }, null, 1));
} finally { page?.close(); await shutdown(browser); }
