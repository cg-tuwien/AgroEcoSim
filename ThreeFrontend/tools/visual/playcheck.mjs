// Validates a video by actually decoding it in Chromium: load into <video>, seek, and draw
// a frame to a canvas. A container that merely has the right magic bytes but a malformed
// index will fail here, which is exactly the failure a byte-level check would miss.
import { readFileSync } from 'node:fs';
import { launch, newPage, sleep, shutdown } from './cdp.mjs';

const file = process.argv[2];
const mime = process.argv[3] || 'video/webm';
const dataUrl = `data:${mime};base64,` + readFileSync(file).toString('base64');

const browser = await launch(+(process.env.CDP_PORT || 9910));
let page;
try {
  page = await newPage(browser.port);
  await page.ready;
  await page.send('Runtime.enable');
  const r = await page.send('Runtime.evaluate', {
    awaitPromise: true, returnByValue: true,
    expression: `(async () => {
      const v = document.createElement('video');
      v.muted = true;
      const ready = new Promise((res) => {
        v.onloadedmetadata = () => res('metadata');
        v.onerror = () => res('error:' + (v.error ? v.error.code + ' ' + v.error.message : 'unknown'));
        setTimeout(() => res('timeout'), 8000);
      });
      v.src = ${JSON.stringify(dataUrl)};
      const how = await ready;
      if (how !== 'metadata') return { ok: false, how };

      // Seek to the first frame and draw it: proves the track really decodes.
      const drawn = await new Promise(res => {
        v.onseeked = () => {
          try {
            const c = document.createElement('canvas');
            c.width = v.videoWidth; c.height = v.videoHeight;
            c.getContext('2d').drawImage(v, 0, 0);
            const d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
            const uniq = new Set();
            for (let i = 0; i < d.length; i += 4) uniq.add(d[i] + ',' + d[i+1] + ',' + d[i+2]);
            res({ drew: true, distinctColours: uniq.size });
          } catch (e) { res({ drew: false, error: String(e) }); }
        };
        v.currentTime = 0;
        setTimeout(() => res({ drew: false, error: 'seek timeout' }), 8000);
      });

      return {
        ok: true, how,
        width: v.videoWidth, height: v.videoHeight,
        duration: v.duration,
        readyState: v.readyState,
        ...drawn,
      };
    })()`,
  });
  console.log(JSON.stringify(r.result.value, null, 1));
} finally { page?.close(); await shutdown(browser); }
