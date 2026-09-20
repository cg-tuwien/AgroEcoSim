// Minimal Chrome DevTools Protocol driver. No dependencies: Node 22 provides both
// global fetch and a global WebSocket, which is all CDP needs.
import { spawn } from 'node:child_process';
import { writeFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const CHROME = process.env.CHROME || '/usr/bin/chromium-browser';

export async function launch(port = 9333) {
  const profile = mkdtempSync(join(tmpdir(), 'cdp-'));
  const args = [
    '--headless=new',
    `--remote-debugging-port=${port}`,
    `--user-data-dir=${profile}`,
    '--no-sandbox', '--disable-dev-shm-usage',
    // Software rasterisation: headless has no GPU, but the app needs a real WebGL2
    // context or the renderer never mounts and we would be screenshotting a blank page.
    '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader',
    '--disable-lcd-text',              // keeps text rendering deterministic between runs
    '--force-device-scale-factor=1',
    '--hide-scrollbars',
    //Extra flags for experiments, e.g. trying to coax a WebGPU adapter out of headless.
    ...(process.env.CHROME_EXTRA_FLAGS ? process.env.CHROME_EXTRA_FLAGS.split(' ').filter(Boolean) : []),
    'about:blank',
  ];
  const proc = spawn(CHROME, args, { stdio: ['ignore', 'ignore', 'pipe'] });
  let stderr = '';
  proc.stderr.on('data', d => { stderr += d.toString(); });

  for (let i = 0; i < 100; ++i) {
    try {
      const r = await fetch(`http://127.0.0.1:${port}/json/version`);
      if (r.ok) return { proc, port, stderr: () => stderr };
    } catch { /* not up yet */ }
    await new Promise(r => setTimeout(r, 200));
  }
  proc.kill('SIGKILL');
  throw new Error(`chromium did not expose a debugging port.\n${stderr}`);
}

export async function newPage(port) {
  const r = await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' });
  const target = await r.json();
  return connect(target.webSocketDebuggerUrl);
}

function connect(url) {
  const ws = new WebSocket(url);
  let nextId = 1;
  const pending = new Map();
  const listeners = [];

  const ready = new Promise((res, rej) => {
    ws.addEventListener('open', () => res());
    ws.addEventListener('error', e => rej(new Error('ws error: ' + e.message)));
  });

  ws.addEventListener('message', ev => {
    const msg = JSON.parse(ev.data);
    if (msg.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id);
      pending.delete(msg.id);
      msg.error ? reject(new Error(JSON.stringify(msg.error))) : resolve(msg.result);
    } else if (msg.method) {
      listeners.forEach(fn => fn(msg));
    }
  });

  return {
    ready,
    send(method, params = {}) {
      const id = nextId++;
      return new Promise((resolve, reject) => {
        pending.set(id, { resolve, reject });
        ws.send(JSON.stringify({ id, method, params }));
      });
    },
    on(fn) { listeners.push(fn); },
    close() { try { ws.close(); } catch { /* already gone */ } },
  };
}

/** Collects console output and uncaught exceptions for the lifetime of the page. */
export function collectDiagnostics(page) {
  const logs = [];
  page.on(msg => {
    if (msg.method === 'Runtime.consoleAPICalled') {
      const text = (msg.params.args || [])
        .map(a => a.value ?? a.description ?? a.unserializableValue ?? `[${a.type}]`)
        .join(' ');
      logs.push({ level: msg.params.type, text });
    } else if (msg.method === 'Runtime.exceptionThrown') {
      const d = msg.params.exceptionDetails;
      logs.push({ level: 'exception', text: d.exception?.description || d.text });
    } else if (msg.method === 'Log.entryAdded') {
      logs.push({ level: msg.params.entry.level, text: msg.params.entry.text });
    }
  });
  return logs;
}

export async function screenshot(page, path) {
  const { data } = await page.send('Page.captureScreenshot', { format: 'png', fromSurface: true });
  writeFileSync(path, Buffer.from(data, 'base64'));
  return path;
}

export const sleep = ms => new Promise(r => setTimeout(r, ms));

/**
 * Shuts the browser down.
 *
 * /usr/bin/chromium-browser is a snap wrapper, so killing the direct child raises EACCES.
 * Browser.close over CDP is the reliable route; the SIGKILL is only a backstop.
 */
export async function shutdown(browser) {
  try {
    const r = await fetch(`http://127.0.0.1:${browser.port}/json/version`);
    const { webSocketDebuggerUrl } = await r.json();
    const ws = new WebSocket(webSocketDebuggerUrl);
    await new Promise(res => ws.addEventListener('open', res));
    ws.send(JSON.stringify({ id: 1, method: 'Browser.close' }));
    await sleep(400);
    ws.close();
  } catch { /* already gone */ }
  try { browser.proc.kill('SIGTERM'); } catch { /* snap wrapper, not ours to kill */ }
}

/**
 * Page script that sets the viewport lighting switches to what a legacy tier number meant.
 *
 * The UI is four independent checkboxes now, but every harness script and every recorded
 * measurement is keyed on the old 0..4 ladder, so the translation lives here rather than being
 * copied into each script. Mirrors lightingFromLegacyTier in core/Lighting.ts.
 */
export function lightingScript(tier, daylight = false, translucency = false) {
  const t = Number(tier) || 0;
  //Translucency is NOT part of the ladder and defaults off, so every recorded tier measurement
  //stays comparable. Pass it explicitly to measure the term itself.
  const want = { lightDaylight: !!daylight, lightShadows: t >= 1, lightOcclusion: t >= 2, lightIndirect: t >= 3, lightSecondBounce: t >= 4, lightLeafTranslucency: !!translucency };
  return `(() => {
    const want = ${JSON.stringify(want)};
    const out = {};
    for (const [name, v] of Object.entries(want)) {
      const el = document.querySelector('[name=' + name + ']');
      if (!el) { out[name] = 'missing'; continue; }
      if (el.checked !== v) {
        el.checked = v;
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.dispatchEvent(new Event('change', { bubbles: true }));
      }
      out[name] = 'ok';
    }
    return out;
  })()`;
}
