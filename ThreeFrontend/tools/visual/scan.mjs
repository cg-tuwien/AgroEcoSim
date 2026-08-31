// Reports the colour census of a PNG's viewport area, and the bounding box of anything that
// is neither page background nor terrain - i.e. "is the plant drawn, and where?".
import { readFileSync } from 'node:fs';
import { launch, newPage, shutdown } from './cdp.mjs';

const src = process.argv[2];
const dataUrl = 'data:image/png;base64,' + readFileSync(src).toString('base64');
const browser = await launch(+(process.env.CDP_PORT || 9380));
let page;
try {
  page = await newPage(browser.port);
  await page.ready;
  await page.send('Runtime.enable');
  const r = await page.send('Runtime.evaluate', {
    awaitPromise: true, returnByValue: true,
    expression: `(async () => {
      const img = await new Promise((res, rej) => { const i = new Image(); i.onload = () => res(i); i.onerror = rej; i.src = ${JSON.stringify(dataUrl)}; });
      const c = document.createElement('canvas');
      c.width = img.width; c.height = img.height;
      const g = c.getContext('2d', { willReadFrequently: true });
      g.drawImage(img, 0, 0);
      const d = g.getImageData(0, 0, c.width, c.height).data;

      const counts = new Map();
      let minX = 1e9, minY = 1e9, maxX = -1, maxY = -1, other = 0;
      for (let y = 0; y < c.height; ++y)
        for (let x = 0; x < c.width; ++x) {
          const i = (y * c.width + x) * 4;
          const key = d[i] + ',' + d[i+1] + ',' + d[i+2];
          counts.set(key, (counts.get(key) || 0) + 1);
          // Ignore the page background, the HUD text area (left column) and the terrain brown.
          const isBg = d[i] > 235 && d[i+1] > 235 && d[i+2] > 235;
          const isTerrain = Math.abs(d[i]-149) < 30 && Math.abs(d[i+1]-96) < 30 && d[i+2] < 40;
          if (!isBg && !isTerrain && x > 340) {
            other++;
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
          }
        }
      const top = Array.from(counts.entries()).sort((a,b) => b[1]-a[1]).slice(0, 8);
      return { size: c.width + 'x' + c.height, distinct: counts.size, top,
               nonBgNonTerrainPixels: other,
               bbox: maxX < 0 ? null : { minX, minY, maxX, maxY } };
    })()`,
  });
  console.log(process.argv[2].split('/').pop(), JSON.stringify(r.result.value, null, 1));
} finally { page?.close(); await shutdown(browser); }
