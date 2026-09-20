//Ray-marches the leaf-area-density field so it can be looked at directly.
//
//This exists because the field is otherwise invisible until the cone trace consumes it, and a
//wrong field and a wrong trace look identical from the final image. Seeing the density
//separates the two.

export const VOXEL_DEBUG_WGSL = /* wgsl */`
struct Debug {
  //xyz: camera world position.
  eye     : vec4f,
  //Ray basis, already scaled by tan(fovY/2) and aspect, so a pixel's direction is
  //right*ndc.x + up*ndc.y + forward.
  right   : vec4f,
  up      : vec4f,
  forward : vec4f,
  //xyz: grid origin. w: unused.
  origin  : vec4f,
  //xyz: world units per cell. w: unused.
  cell    : vec4f,
  //xyz: the grid's world extent. w: unused. Sent directly because the grid is not a cube.
  extent  : vec4f,
  //x: mip level, y: opacity gain, z: 1 = show albedo, 0 = show density as a ramp.
  params  : vec4f,
};

@group(0) @binding(0) var<uniform> dbg : Debug;
@group(0) @binding(1) var field : texture_3d<f32>;
@group(0) @binding(2) var fieldSampler : sampler;

struct VSOut {
  @builtin(position) position : vec4f,
  @location(0) ndc : vec2f,
};

@vertex
fn vs(@builtin(vertex_index) vi : u32) -> VSOut {
  //One oversized triangle rather than a quad: no diagonal seam, one fewer vertex.
  var pts = array<vec2f, 3>(vec2f(-1.0, -3.0), vec2f(-1.0, 1.0), vec2f(3.0, 1.0));
  var o : VSOut;
  o.position = vec4f(pts[vi], 0.0, 1.0);
  o.ndc = pts[vi];
  return o;
}

//Slab test against the grid's bounding box. Returns (tEnter, tExit); tExit < tEnter misses.
fn intersectBox(ro : vec3f, rd : vec3f, lo : vec3f, hi : vec3f) -> vec2f {
  let inv = 1.0 / rd;
  let t0 = (lo - ro) * inv;
  let t1 = (hi - ro) * inv;
  let tmin = min(t0, t1);
  let tmax = max(t0, t1);
  return vec2f(max(max(tmin.x, tmin.y), tmin.z), min(min(tmax.x, tmax.y), tmax.z));
}

//Density mapped to a legible ramp: dark blue is sparse, yellow is dense.
fn ramp(t : f32) -> vec3f {
  let x = clamp(t, 0.0, 1.0);
  return vec3f(clamp(x * 2.0, 0.0, 1.0), clamp(x * 1.6 - 0.25, 0.0, 1.0), clamp(0.6 - x * 1.2, 0.0, 1.0));
}

@fragment
fn fs(i : VSOut) -> @location(0) vec4f {
  let rd = normalize(dbg.right.xyz * i.ndc.x + dbg.up.xyz * i.ndc.y + dbg.forward.xyz);
  let ro = dbg.eye.xyz;

  let lo = dbg.origin.xyz;
  let hi = lo + dbg.extent.xyz;
  let hit = intersectBox(ro, rd, lo, hi);
  let tEnter = max(hit.x, 0.0);
  if (hit.y <= tEnter) { discard; }

  let mip = dbg.params.x;
  //Step at one cell of the level being shown, so a coarse mip is not oversampled.
  let step = min(dbg.cell.x, min(dbg.cell.y, dbg.cell.z)) * exp2(mip);
  let span = hit.y - tEnter;
  let count = clamp(i32(ceil(span / step)), 1, 512);

  var accum = vec3f(0.0);
  var transmittance = 1.0;
  for (var s = 0; s < count; s++) {
    let t = tEnter + (f32(s) + 0.5) * step;
    let p = ro + rd * t;
    let uvw = (p - lo) / dbg.extent.xyz;
    let texel = textureSampleLevel(field, fieldSampler, uvw, mip);

    let sigma = texel.a * dbg.params.y;
    if (sigma <= 0.0) { continue; }

    //Beer-Lambert over the step, the same law the cone trace will use.
    let absorbed = 1.0 - exp(-sigma * step);
    //rgb is sigma-premultiplied radiance, so dividing by sigma recovers the albedo.
    let albedo = texel.rgb / max(texel.a, 1e-6);
    let shown = select(ramp(sigma * 0.05), albedo, dbg.params.z > 0.5);
    accum += transmittance * absorbed * shown;
    transmittance *= 1.0 - absorbed;
    if (transmittance < 0.01) { break; }
  }

  let alpha = 1.0 - transmittance;
  if (alpha <= 0.001) { discard; }
  //Premultiplied, matching the src-alpha-one blend state the pipeline declares.
  return vec4f(accum, alpha);
}
`;
