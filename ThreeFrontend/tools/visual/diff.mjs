// Pixel-diffs two PNGs by decoding them inside chromium, so no image library is needed.
import { readFileSync, writeFileSync } from 'node:fs';
import { launch, newPage, sleep, shutdown } from './cdp.mjs';

const [a, b, outDiff] = process.argv.slice(2);
const toDataUrl = p => 'data:image/png;base64,' + readFileSync(p).toString('base64');

const browser = await launch(+(process.env.CDP_PORT || 9335));
let page;
try {
  page = await newPage(browser.port);
  await page.ready;
  await page.send('Runtime.enable');

  const r = await page.send('Runtime.evaluate', {
    awaitPromise: true,
    returnByValue: true,
    expression: `(async () => {
      const load = src => new Promise((res, rej) => {
        const i = new Image(); i.onload = () => res(i); i.onerror = rej; i.src = src;
      });
      const A = await load(${JSON.stringify(toDataUrl(a))});
      const B = await load(${JSON.stringify(toDataUrl(b))});
      if (A.width !== B.width || A.height !== B.height)
        return { error: 'size mismatch', a: [A.width, A.height], b: [B.width, B.height] };

      const w = A.width, h = A.height;
      const px = img => {
        const c = document.createElement('canvas');
        c.width = w; c.height = h;
        const x = c.getContext('2d', { willReadFrequently: true });
        x.drawImage(img, 0, 0);
        return x.getImageData(0, 0, w, h).data;
      };
      const pa = px(A), pb = px(B);

      // Highlight differing pixels in magenta over a dimmed copy, so the diff is readable.
      const dc = document.createElement('canvas');
      dc.width = w; dc.height = h;
      const dctx = dc.getContext('2d');
      const dimg = dctx.createImageData(w, h);

      let differing = 0, maxDelta = 0, sumDelta = 0;
      //Direction, summed over channels. A pass that is supposed to be strictly additive - the
      //GI second bounce - is only checkable if the diff can say which way a pixel moved.
      let brighter = 0, darker = 0;
      const hist = {};
      for (let i = 0; i < pa.length; i += 4) {
        const dr = Math.abs(pa[i] - pb[i]);
        const dg = Math.abs(pa[i+1] - pb[i+1]);
        const db = Math.abs(pa[i+2] - pb[i+2]);
        const d = Math.max(dr, dg, db);
        if (d > 0) {
          differing++; sumDelta += d;
          if (d > maxDelta) maxDelta = d;
          const signed = (pb[i] - pa[i]) + (pb[i+1] - pa[i+1]) + (pb[i+2] - pa[i+2]);
          if (signed > 0) brighter++; else if (signed < 0) darker++;
          const bucket = d <= 1 ? '1' : d <= 2 ? '2' : d <= 4 ? '4' : d <= 8 ? '8' : d <= 32 ? '32' : 'big';
          hist[bucket] = (hist[bucket] || 0) + 1;
          dimg.data[i] = 255; dimg.data[i+1] = 0; dimg.data[i+2] = 255; dimg.data[i+3] = 255;
        } else {
          dimg.data[i] = pa[i] * 0.25 + 190;
          dimg.data[i+1] = pa[i+1] * 0.25 + 190;
          dimg.data[i+2] = pa[i+2] * 0.25 + 190;
          dimg.data[i+3] = 255;
        }
      }
      dctx.putImageData(dimg, 0, 0);

      return {
        width: w, height: h,
        totalPixels: w * h,
        differing,
        percent: +(100 * differing / (w * h)).toFixed(4),
        maxDelta,
        //Relative to the FIRST image: how many differing pixels got brighter in the second.
        brighter, darker,
        meanDeltaOfDiffering: differing ? +(sumDelta / differing).toFixed(2) : 0,
        histogram: hist,
        diffPng: dc.toDataURL('image/png'),
      };
    })()`,
  });

  const v = r.result.value;
  if (v.diffPng) {
    writeFileSync(outDiff, Buffer.from(v.diffPng.split(',')[1], 'base64'));
    delete v.diffPng;
  }
  console.log(JSON.stringify(v, null, 2));
} finally {
  page?.close();
  await shutdown(browser);
}
