// Crops and upscales a region of a PNG using chromium's canvas, so small on-screen detail
// can actually be inspected. No image library needed.
import { readFileSync, writeFileSync } from 'node:fs';
import { launch, newPage, sleep, shutdown } from './cdp.mjs';

const [src, out, x, y, w, h, scale] = process.argv.slice(2);
const dataUrl = 'data:image/png;base64,' + readFileSync(src).toString('base64');

const browser = await launch(+(process.env.CDP_PORT || 9360));
let page;
try {
  page = await newPage(browser.port);
  await page.ready;
  await page.send('Runtime.enable');
  const r = await page.send('Runtime.evaluate', {
    awaitPromise: true, returnByValue: true,
    expression: `(async () => {
      const img = await new Promise((res, rej) => { const i = new Image(); i.onload = () => res(i); i.onerror = rej; i.src = ${JSON.stringify(dataUrl)}; });
      const S = ${scale || 6}, X = ${x}, Y = ${y}, W = ${w}, H = ${h};
      const c = document.createElement('canvas');
      c.width = W * S; c.height = H * S;
      const g = c.getContext('2d');
      g.imageSmoothingEnabled = false;   // nearest neighbour, so pixels stay honest
      g.drawImage(img, X, Y, W, H, 0, 0, W * S, H * S);
      // Report the colour spread so "is anything there" is answerable numerically.
      const d = g.getImageData(0, 0, c.width, c.height).data;
      const uniq = new Set();
      for (let i = 0; i < d.length; i += 4) uniq.add(d[i] + ',' + d[i+1] + ',' + d[i+2]);
      return { png: c.toDataURL('image/png'), distinctColours: uniq.size, sample: Array.from(uniq).slice(0, 12) };
    })()`,
  });
  const v = r.result.value;
  writeFileSync(out, Buffer.from(v.png.split(',')[1], 'base64'));
  console.log(JSON.stringify({ distinctColours: v.distinctColours, sample: v.sample }, null, 2));
} finally {
  page?.close();
  await shutdown(browser);
}
