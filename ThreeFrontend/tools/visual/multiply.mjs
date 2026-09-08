// Replicates a saved session's plants N times, spread across the field, producing a fixture at
// production scale (the test site has 7,700 living plants) without running the simulator for
// weeks. Parses the V7 scene stream so each copy's affine translation can be offset - stacking
// every plant at one point would understate draw-call and buffer pressure and wildly overstate
// atomic contention in the voxel field.
import { readFileSync, writeFileSync } from 'node:fs';

const [inPath, outPath, countArg] = process.argv.slice(2);
const COPIES = +(countArg || 7700);

const ORG = { Bud: 2, Stem: 4, Leaf: 5, Petiole: 6, Meristem: 8, RizomeMeristem: 9,
              FlowerBud: 10, FlowerStem: 11, FlowerPadel: 12, FlowerPedicel: 13, FlowerMeristem: 14 };
const CYL = new Set([ORG.Stem, ORG.Petiole, ORG.Meristem, ORG.RizomeMeristem,
                     ORG.FlowerBud, ORG.FlowerStem, ORG.FlowerPedicel, ORG.FlowerMeristem]);
const LAM = new Set([ORG.Leaf, ORG.FlowerPadel]);

class R {
  constructor(b) { this.b = b; this.v = new DataView(b.buffer, b.byteOffset, b.byteLength); this.p = 0; }
  u8() { return this.b[this.p++]; }
  i8() { const r = this.u8(); return r >= 128 ? 256 - r : r; }
  u32() { const r = this.v.getUint32(this.p, true); this.p += 4; return r; }
  i32() { const r = this.v.getInt32(this.p, true); this.p += 4; return r; }
  skip(n) { this.p += n; }
  str() { let res = 0, sh = 0, x; do { x = this.b[this.p++]; res |= (x & 0x7f) << sh; sh += 7; } while (x & 0x80); this.p += res; }
}

// Walks one entity, recording the byte offset of every affine translation component so the
// copies can be displaced. Offsets are relative to the entity block's start.
function scanEntity(r, base, doRoots, doAnalytics, xs) {
  r.str();                                   // species
  const prims = r.u32();
  for (let i = 0; i < prims; ++i) {
    r.i32();                                 // parentIndex
    const organ = r.u8();
    if (LAM.has(organ)) {
      xs.push(r.p - base);                   // start of the 12-float affine transform
      r.skip(48);
      r.skip(doAnalytics ? 31 : 3);
    } else if (CYL.has(organ)) {
      r.skip(8);                             // length, radius
      xs.push(r.p - base);
      r.skip(48);
      r.skip(doAnalytics ? 31 : 3);
    } else if (organ === ORG.Bud) {
      r.skip(16);
      if (doAnalytics) r.skip(16);
    } else {
      throw new Error(`unknown organ ${organ} at primitive ${i}`);
    }
  }
  if (doRoots) {
    const virtual = r.u8() !== 0;
    if (virtual) r.skip(12);
    const roots = virtual ? 0 : r.u32();
    for (let j = 0; j < roots; ++j) {
      r.i32();
      const code = r.u8();
      if (code === 1) {
        r.skip(8);
        xs.push(r.p - base);
        r.skip(48);
        if (doAnalytics) r.skip(20);
      }
    }
  }
  return r.p - base;
}

const session = JSON.parse(readFileSync(inPath, 'utf8'));
const spanX = session.fieldSizeX || 10, spanZ = session.fieldSizeZ || 10;

const last = session.history.reduce((a, b) => (b.s.length > a.s.length ? b : a));
const src = new Uint8Array(Buffer.from(last.s, 'base64'));
const r = new R(src);
//Header is version(1) + flags(1) + entityCount(4) - hence the 6-byte empty scene.
const version = r.u8();
if (version !== 7) throw new Error(`scene format v${version}, this tool understands v7 only`);
const flags = r.i8();
const doRoots = (flags & 1) > 0, doAnalytics = (flags & 2) > 0;
const entities = r.u32();
const headerEnd = r.p;

// Record every entity's byte range and its translation-component offsets.
const blocks = [];
for (let e = 0; e < entities; ++e) {
  const base = r.p;
  const xs = [];
  const len = scanEntity(r, base, doRoots, doAnalytics, xs);
  blocks.push({ base, len, xs });
}
if (r.p !== src.length) throw new Error(`parse ended at ${r.p} of ${src.length} - format mismatch`);

const perCopy = src.length - headerEnd;
const out = Buffer.alloc(headerEnd + perCopy * COPIES);
src.subarray(0, headerEnd).forEach((b, i) => (out[i] = b));
out.writeUInt32LE(entities * COPIES, 2);

// Lay the copies out on a square grid covering the field, so the density field sees a real
// canopy rather than one pathologically dense cell.
const side = Math.ceil(Math.sqrt(COPIES));
let at = headerEnd;
for (let c = 0; c < COPIES; ++c) {
  const dx = ((c % side) / side - 0.5) * spanX;
  const dz = (Math.floor(c / side) / side - 0.5) * spanZ;
  src.subarray(headerEnd).forEach((b, i) => (out[at + i] = b));
  for (const blk of blocks) {
    const blockAt = at + (blk.base - headerEnd);
    for (const off of blk.xs) {
      //Row-major 3x4, so the rows are (m00 m01 m02 tx | m10 m11 m12 ty | m20 m21 m22 tz):
      //translation is at float indices 3 and 11, i.e. byte 12 and byte 44 from the start of the
      //transform. Patching byte 0 and byte 32 instead hits m00 and m20 - which does not move a
      //plant, it shears it, and 7,700 sheared copies still look plausibly like a dense canopy.
      const px = blockAt + off + 12, pz = blockAt + off + 44;
      out.writeFloatLE(out.readFloatLE(px) + dx, px);
      out.writeFloatLE(out.readFloatLE(pz) + dz, pz);
    }
  }
  at += perCopy;
}

session.history = [{ t: last.t, s: out.toString('base64') }];
session.historySize = out.length;
writeFileSync(outPath, JSON.stringify(session));
console.log(JSON.stringify({
  version, doRoots, doAnalytics,
  sourceEntities: entities, copies: COPIES, totalPlants: entities * COPIES,
  translationsPerCopy: blocks.reduce((n, b) => n + b.xs.length, 0),
  payloadBytes: out.length, base64Bytes: session.history[0].s.length,
  fieldSpan: [spanX, spanZ],
}, null, 1));
