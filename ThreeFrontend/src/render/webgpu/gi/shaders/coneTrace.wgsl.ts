//Voxel cone tracing: the soft, far-field half of the lighting.
//
//Cone tracing is used rather than anything that accumulates over frames because the scene's
//geometry is REPLACED every timestep - `readAgroSceneV7` rebuilds the plant list from scratch
//with no inter-frame identity - so a temporal estimator has nothing stable to accumulate onto
//and would smear green trails behind growing organs. A fixed set of cone directions is
//deterministic and noise-free in a single frame, which is the property that matters here.

export const CONE_TRACE_WGSL = /* wgsl */`
struct Trace {
  //Rebuilds a view position from linear depth: xy = tan(fovY/2)*aspect, tan(fovY/2).
  //zw = the resolution the trace runs at.
  projParams : vec4f,
  //xy = the full-resolution size the gbuffer was rendered at. z = cone length in metres.
  //w = indirect intensity.
  settings   : vec4f,
  //xyz: grid origin. w: cells per axis.
  gridOrigin : vec4f,
  //xyz: world units per cell. w: unused.
  gridCell   : vec4f,
  //xyz: direction TO the sun, world space. w: 1 when the sun should be traced at all.
  sunDir     : vec4f,
  //Camera world matrix, for taking the reconstructed view position back to world space.
  invView    : mat4x4f,
};

@group(0) @binding(0) var<uniform> trace : Trace;
@group(0) @binding(1) var gNormal : texture_2d<f32>;
@group(0) @binding(2) var gDepth : texture_2d<f32>;
@group(0) @binding(3) var field : texture_3d<f32>;
@group(0) @binding(4) var fieldSampler : sampler;
//rgb: bounced radiance. a: the fraction of the hemisphere that is NOT occluded.
@group(0) @binding(5) var dst : texture_storage_2d<rgba16float, write>;

const PI : f32 = 3.14159265;

fn octDecode(e : vec2f) -> vec3f {
  var v = vec3f(e.xy, 1.0 - abs(e.x) - abs(e.y));
  if (v.z < 0.0) {
    let s = vec2f(select(-1.0, 1.0, v.x >= 0.0), select(-1.0, 1.0, v.y >= 0.0));
    v = vec3f((vec2f(1.0) - abs(v.yx)) * s, v.z);
  }
  return normalize(v);
}

fn viewPosFrom(uv : vec2f, dist : f32) -> vec3f {
  let ndc = vec2f(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
  return vec3f(ndc.x * trace.projParams.x, ndc.y * trace.projParams.y, -1.0) * dist;
}

/**
 * Marches one cone and returns (radiance, occlusion).
 *
 * Beer-Lambert per step over the sigma stored in the field. The mip level follows the cone's
 * diameter, which is the whole point of a cone trace: a wide cone reads a coarse level in one
 * sample instead of integrating hundreds of fine cells.
 */
//The origin is expected to be lifted clear of the surface's own cell; see coneOrigin.
fn traceCone(origin : vec3f, dir : vec3f, aperture : f32) -> vec4f {
  let cell = min(trace.gridCell.x, min(trace.gridCell.y, trace.gridCell.z));
  let dim = trace.gridOrigin.w;
  let extent = trace.gridCell.xyz * dim;
  let lo = trace.gridOrigin.xyz;

  var radiance = vec3f(0.0);
  var transmittance = 1.0;
  var dist = cell;
  let maxDist = trace.settings.z;

  for (var i = 0; i < 24; i++) {
    if (dist >= maxDist || transmittance < 0.02) { break; }

    let diameter = max(cell, 2.0 * aperture * dist);
    let mip = clamp(log2(diameter / cell), 0.0, 6.0);
    let p = origin + dir * dist;

    let uvw = (p - lo) / extent;
    //Outside the grid there is nothing to occlude and nothing to bleed: the cone sees sky.
    if (any(uvw < vec3f(0.0)) || any(uvw > vec3f(1.0))) { break; }

    let texel = textureSampleLevel(field, fieldSampler, uvw, mip);
    let sigma = texel.a;
    if (sigma > 0.0) {
      let step = diameter;
      let absorbed = 1.0 - exp(-sigma * step);
      //rgb is sigma-premultiplied radiance, so dividing by sigma recovers the radiance the
      //cell emits; the absorbed fraction is how much of this step's path it covers.
      radiance += transmittance * absorbed * (texel.rgb / sigma);
      transmittance *= 1.0 - absorbed;
    }
    dist += max(cell, diameter) * 0.5;
  }

  return vec4f(radiance, 1.0 - transmittance);
}

//Where a cone starts: clear of the cell the surface itself was injected into.
//
//A surface contributes its own area to the voxels around it, so tracing from the exact
//surface point makes every surface occlude itself. One cell is enough for a leaf, which is
//thin. It was two while the terrain was still being injected, because a ground plane needed
//far more clearance - but a whole cell of offset is a big fraction of a plant only a few
//cells tall, and it was throwing away most of the near-field shading that makes the tier
//worth having. Terrain is out of the field now, so the clearance can come back down.
fn coneOrigin(world : vec3f, n : vec3f) -> vec3f {
  let cell = min(trace.gridCell.x, min(trace.gridCell.y, trace.gridCell.z));
  return world + n * cell;
}

/**
 * Sun transmittance: how much direct sunlight survives the canopy above this point.
 *
 * This replaces the shadow map entirely. A shadow map compares a fragment's depth against the
 * nearest surface the light can see, and a leaf is a zero-thickness quad - caster and receiver
 * are the same triangle, so the comparison is a tie that only a bias can break, and a leaf ends
 * up casting a leaf-shaped shadow onto itself. There is no comparison here at all: the density
 * field says how much leaf area lies between this point and the sun, and Beer-Lambert turns
 * that into a fraction. A surface cannot occlude itself because its own cell contributes only
 * its own small sigma over one cell, and the march starts clear of it.
 *
 * It is also the same law the simulation's own light transport uses, so a shadow here means
 * what a shadow means to the model underneath.
 */
fn traceSun(origin : vec3f, n : vec3f) -> f32 {
  if (trace.sunDir.w < 0.5) { return 1.0; }

  let dir = trace.sunDir.xyz;
  let cellSize = min(trace.gridCell.x, min(trace.gridCell.y, trace.gridCell.z));
  let extent = trace.gridCell.xyz * trace.gridOrigin.w;
  let lo = trace.gridOrigin.xyz;

  //Start two cells along the SUN DIRECTION, and only along it.
  //
  //Offsetting along the surface normal turned towards the sun seems natural and is a trap: the
  //turn is a sign flip at dot(n, dir) == 0, so with a low sun - where many leaf triangles sit
  //near that zero - neighbouring triangles of one leaf start their march two cells apart on
  //OPPOSITE sides. One comes back shadowed and its neighbour lit, which is the faceted, creepy
  //look on young leaves; and as the sun moves an hour, whole groups cross the zero together
  //and the shadow pattern jumps rather than sweeping.
  //
  //Marching along the sun direction has no such discontinuity - it always moves towards the
  //light - and two cells still clears the origin cell, which is what the offset was for. A
  //leaf is far smaller than a cell, so its own area smears over the cell it sits in and, via
  //trilinear filtering, into the neighbours; anything less lets a leaf shadow itself.
  var dist = cellSize * 2.0;
  var transmittance = 1.0;

  //A narrow cone, not a ray: the sun has an angular size and a canopy edge should not be a
  //hard line at this voxel resolution. The widening also hides the grid.
  let aperture = 0.05;

  for (var i = 0; i < 20; i++) {
    if (transmittance < 0.02) { break; }
    let diameter = max(cellSize, 2.0 * aperture * dist);
    let mip = clamp(log2(diameter / cellSize), 0.0, 6.0);
    let probe = origin + dir * dist;
    let uvw = (probe - lo) / extent;
    //Out of the grid means out of the canopy: nothing above, so full sun.
    if (any(uvw < vec3f(0.0)) || any(uvw > vec3f(1.0))) { break; }

    let sigma = textureSampleLevel(field, fieldSampler, uvw, mip).a;
    if (sigma > 0.0) {
      transmittance *= exp(-sigma * diameter);
    }
    dist += max(cellSize, diameter);
  }

  return clamp(transmittance, 0.0, 1.0);
}

/** Any unit vector perpendicular to n, chosen without a branch that can degenerate. */
fn tangentTo(n : vec3f) -> vec3f {
  let up = select(vec3f(0.0, 1.0, 0.0), vec3f(1.0, 0.0, 0.0), abs(n.y) > 0.9);
  return normalize(cross(up, n));
}

@compute @workgroup_size(8, 8)
fn main(@builtin(global_invocation_id) gid : vec3u) {
  let size = vec2u(u32(trace.projParams.z), u32(trace.projParams.w));
  if (gid.x >= size.x || gid.y >= size.y) { return; }

  let full = vec2i(i32(gid.x) * 2, i32(gid.y) * 2);
  let dist = textureLoad(gDepth, full, 0).x;
  //Nothing was drawn here. Full visibility, no bounce: the background must be untouched.
  if (dist <= 0.0) {
    //Nothing drawn here: no bounce, and full sun so the background is never darkened.
    textureStore(dst, vec2i(gid.xy), vec4f(0.0, 0.0, 0.0, 1.0));
    return;
  }

  let uv = (vec2f(f32(full.x), f32(full.y)) + 0.5) / trace.settings.xy;
  let viewPos = viewPosFrom(uv, dist);
  let world = (trace.invView * vec4f(viewPos, 1.0)).xyz;
  let viewNormal = octDecode(textureLoad(gNormal, full, 0).xy);
  //The gbuffer normal is in view space and the field is in world space.
  let n = normalize((trace.invView * vec4f(viewNormal, 0.0)).xyz);

  let t = tangentTo(n);
  let b = cross(n, t);

  //Five cones: one up the normal and four tilted off it, the standard diffuse arrangement.
  //The weights are normalised to sum to one, so an unoccluded point returns occlusion 0 and
  //the composite reduces exactly to the tier below.
  //
  //Tilted 50 degrees, not the textbook 60. With a 30-degree half-angle a 60-degree cone has
  //its lower edge exactly horizontal, so on flat ground it grazes the surface for its whole
  //length and an open field reads as permanently overcast. At 50 the edge clears by 10.
  let side = 0.642788;     // cos 50
  let out = 0.766044;      // sin 50
  var radiance = vec3f(0.0);
  var occlusion = 0.0;

  let wCentre = 0.25;
  let wSide = 0.1875;      // 4 * 0.1875 + 0.25 = 1

  let origin = coneOrigin(world, n);

  //The diffuse cones are the expensive half and only the indirect switch asks for them; the
  //sun trace is wanted on its own whenever shadows are on.
  if (trace.settings.w > 0.0) {
    var r = traceCone(origin, n, 0.577);   // tan 30, a 60-degree cone
    radiance += wCentre * r.rgb;
    occlusion += wCentre * r.a;

    for (var i = 0; i < 4; i++) {
      let a = f32(i) * (PI * 0.5);
      let dir = normalize(n * side + (t * cos(a) + b * sin(a)) * out);
      r = traceCone(origin, dir, 0.577);
      radiance += wSide * r.rgb;
      occlusion += wSide * r.a;
    }
  }

  //rgb is the bounce; ALPHA IS SUN TRANSMITTANCE, not sky visibility. The screen-space AO pass
  //already measures sky occlusion and does it at a finer scale than an 8 cm voxel can, so the
  //channel is better spent on the thing the voxels are uniquely good at: how much canopy lies
  //between this point and the sun.
  let sunVis = traceSun(world, n);
  let bounce = radiance * trace.settings.w;
  //Fold the diffuse cones' own occlusion into the bounce rather than exporting it: it and the
  //screen-space AO measure the same quantity, and the composite already takes the darker.
  textureStore(dst, vec2i(gid.xy), vec4f(bounce * clamp(1.0 - occlusion, 0.0, 1.0), sunVis));
}
`;
