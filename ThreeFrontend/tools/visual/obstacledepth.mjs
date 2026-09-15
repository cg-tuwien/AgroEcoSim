// Analytic regression: a mapped panel overlaps differently triangulated context. Camera motion
// must not expose context inside the panel, and a genuinely nearer object must still occlude it.
// Usage: node obstacledepth.mjs 'http://localhost:8080/?gpu=0&debughook=1'
// For gpu=1 use the WebGPU CHROME_EXTRA_FLAGS from README.md.
import { launch, newPage, collectDiagnostics, sleep, shutdown } from './cdp.mjs';

const [url] = process.argv.slice(2);
const browser = await launch(+(process.env.CDP_PORT || 25310));
let page;
const ev = async expression => {
  const r = await page.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  return r.result.value;
};
try {
  page = await newPage(browser.port); await page.ready;
  const logs = collectDiagnostics(page);
  await page.send('Runtime.enable'); await page.send('Log.enable');
  await page.send('Page.navigate', { url });
  for (let i = 0; i < 100; ++i) {
    if (await ev('!!window.__renderer && !!window.__appstate')) break;
    await sleep(200);
  }
  const kind = await ev('window.__renderer.kind');
  if (kind !== (url.includes('gpu=1') ? 'webgpu' : 'three')) throw new Error(`Wrong renderer: ${kind}`);

  await ev(`(() => {
    const a = window.__appstate;
    a.pushRndObstacle();
    const Obstacle = a.obstacles.value[0].constructor;
    const vertices = [], indices = [], groups = [];
    const plane = (x, y) => 0.1334 + x * 0.000028;
    const add = (name, points, triangles, offset = 0) => {
      const first = vertices.length / 3, indexFirst = indices.length;
      //Use facade-scale world coordinates, so the test includes transform roundoff.
      for (const [x, y] of points) vertices.push(x+8, y+10, plane(x, y) + offset);
      for (const i of triangles) indices.push(first + i);
      groups.push({ name, vertexFirst: first, vertexCount: points.length, indexFirst, indexCount: triangles.length });
    };
    add('measured', [[-1,0],[1,0],[1,2],[-1,2]], [0,1,2,0,2,3]);
    add('context', [[-2,-1],[2,-1],[2,3],[-2,3],[0,1]], [0,1,4,1,2,4,2,3,4,3,0,4]);
    add('occluder', [[-0.15,0.7],[0.15,0.7],[0.15,1.3],[-0.15,1.3]], [0,1,2,0,2,3], 0.02);
    const o = new Obstacle('mesh',0,0,0,0,0,1,1,1,new Float32Array(vertices),indices);
    o.groups = groups;
    a.obstacles.value = [o];
    a.showTerrain.value = false; a.showSeeds.value = false;
    window.__depthMapping = { values: new Map([['measured',0]]), range: [0,1] };
    window.__applyDepthMapping = mapping => window.__renderer.rebuildObstacles({ obstacles:[o], visible:true, mapping });
    window.__depthObstacle = o;
    return true;
  })()`);
  await sleep(500);

  const results = await ev(`(async () => {
    const r = window.__renderer, W = 640, H = 400;
    const srgb = c => Math.round(255 * (c <= 0.0031308 ? 12.92*c : 1.055*Math.pow(c,1/2.4)-0.055));
    const sub = (a,b) => a.map((x,i)=>x-b[i]);
    const norm = a => {const l=Math.hypot(...a);return a.map(x=>x/l);};
    const cross = (a,b) => [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]];
    const dot = (a,b) => a.reduce((s,x,i)=>s+x*b[i],0);
    const frames = [];
    for (const pos of [[0,1,3], [0.003,1,3], [1.5,1.6,3], [-1.5,0.7,5], [10,8,30]]) {
      const eye=[pos[0]+8,pos[1]+10,pos[2]];
      const pose = {...r.getCameraPose(), position:{x:eye[0],y:eye[1],z:eye[2]}, target:{x:8,y:11,z:0.1334}};
      r.setCameraPose(pose);
      window.__applyDepthMapping(window.__depthMapping);
      const read = async () => (await r.renderToPixels(W,H,1,1,r.getCameraPose())).data;
      const px = await read();
      const rgb = Array.from(window.__depthObstacle.meshData.colors.slice(0,3),srgb);
      const gray = [0.02,0.02,0.024].map(srgb);
      const forward = norm(sub([8,11,0.1334],eye)), right = norm(cross(forward,[0,1,0])), up = cross(right,forward);
      const scale = H/(2*Math.tan(pose.fovY*Math.PI/360));
      const at = p => {const d=sub([p[0]+8,p[1]+10,p[2]],eye),z=dot(d,forward); return [Math.floor(W/2+dot(d,right)*scale/z),Math.floor(H/2-dot(d,up)*scale/z)];};
      let wrong = 0, checked = 0;
      for(let y=0.15;y<1.9;y+=0.1) for(let x=-0.85;x<0.9;x+=0.1) {
        //Keep well clear of the real occluder's silhouette, even in the oblique views.
        if (Math.abs(x)<0.3 && y>0.5 && y<1.5) continue;
        const [sx,sy]=at([x,y,0.1334+x*0.000028]);
        if(sx<0||sx>=W||sy<0||sy>=H)continue;
        ++checked; const i=4*(sy*W+sx);
        if(rgb.some((c,k)=>Math.abs(c-px[i+k])>1)) ++wrong;
      }
      const [sx,sy]=at([0,1,0.1534]), occluderAt=4*(sy*W+sx);
      const occluded = gray.every((c,k)=>Math.abs(c-px[occluderAt+k])<=1);
      //Changing which named object has data repartitions the index buffer, even when the
      //vertex count is unchanged. The larger context panel now has the depth preference.
      window.__applyDepthMapping({values:new Map([['context',0]]),range:[0,1]});
      const switched=await read(), [cx,cy]=at([0.65,1,0.1334+0.65*0.000028]);
      const membershipCorrect=rgb.every((c,k)=>Math.abs(c-switched[4*(cy*W+cx)+k])<=1);
      window.__applyDepthMapping({values:undefined,range:[0,1]}); await read();
      window.__applyDepthMapping(window.__depthMapping); const restored=await read();
      let changed=0; for(let i=0;i<px.length;i++)if(px[i]!==restored[i])++changed;
      frames.push({pos,checked,wrong,occluded,membershipCorrect,changedAfterToggle:changed});
    }
    return frames;
  })()`);
  const errors = logs.filter(l => l.level === 'exception' || /WebGPU.*(error|lost)|validation error/i.test(l.text));
  console.log(JSON.stringify({kind,results,errors},null,2));
  if (errors.length || results.some(r => r.checked<100 || r.wrong || !r.occluded || !r.membershipCorrect || r.changedAfterToggle))
    throw new Error('Obstacle depth regression');
} finally { page?.close(); await shutdown(browser); }
