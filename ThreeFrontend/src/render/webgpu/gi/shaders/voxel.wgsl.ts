//Voxelisation of the scene into a leaf-area-density field.
//
//WGSL has atomics only on storage BUFFERS, never on storage textures, and WebGPU has no
//conservative rasterisation. So the injection scatters from a compute pass over triangles
//into an atomic buffer, and a second pass resolves that buffer into a 3D texture. That is not
//a workaround for the missing features - it is strictly better here, because what we want is
//AREA, and a rasteriser measures coverage.

/** Shared declarations: the grid, and the fixed-point encoding the atomics use. */
export const VOXEL_COMMON = /* wgsl */`
struct Grid {
  //xyz: world-space minimum corner. w: unused.
  origin : vec4f,
  //xyz: world units per cell. w: 1/cellVolume.
  cell   : vec4f,
  //xyz: cells per axis, as floats. w: unused.
  //
  //Per axis, because the grid is no longer a cube: fitting a box around an imported building
  //rather than cubing it is what makes a domain that covers one affordable. The cells stay
  //cubic, only the counts differ.
  dim    : vec4f,
};

//Atomics are integer-only, so area accumulates in fixed point. Areas here are square metres
//of leaf inside a cubic decimetre, so the scale has to be generous at the small end; 1e6
//leaves ~4300 m2 of headroom per cell before a u32 wraps, which no cell can approach.
const AREA_SCALE : f32 = 1e6;

fn cellOf(g : Grid, p : vec3f) -> vec3i {
  return vec3i(floor((p - g.origin.xyz) / g.cell.xyz));
}

fn insideGrid(g : Grid, c : vec3i) -> bool {
  return all(c >= vec3i(0)) && all(c < vec3i(g.dim.xyz));
}

fn cellIndex(g : Grid, c : vec3i) -> u32 {
  let d = vec3i(g.dim.xyz);
  return u32((c.z * d.y + c.y) * d.x + c.x);
}
`;

/**
 * Injection. One invocation per (triangle, instance) pair.
 *
 * Dispatched once per geometry bucket, with x covering the bucket's triangles and y its
 * instances, so no per-frame job list has to be built on the CPU.
 */
export const VOXEL_INJECT_WGSL = /* wgsl */`${VOXEL_COMMON}

struct Instance {
  model    : mat4x4f,
  n0       : vec4f,
  n1       : vec4f,
  n2       : vec4f,
  color    : vec4f,
  emissive : vec4f,
  pickId   : u32,
  flags    : u32,
  _pad0    : u32,
  _pad1    : u32,
};

struct Bucket {
  //x: firstIndex, y: triangleCount, z: baseVertex, w: firstInstance.
  slice    : vec4u,
  //x: instanceCount, y: sample batches (dispatch z), each worth SAMPLES_PER_INVOCATION.
  counts   : vec4u,
};

@group(0) @binding(0) var<uniform> grid : Grid;
@group(0) @binding(1) var<storage, read> vertices : array<f32>;
@group(0) @binding(2) var<storage, read> indices : array<u32>;
@group(0) @binding(3) var<storage, read> instances : array<Instance>;
//Four channels per cell: area, then colour premultiplied by area. Resolving colour as a
//weighted mean is what lets a cell holding a leaf and a stem bleed the right mixture.
@group(0) @binding(4) var<storage, read_write> field : array<atomic<u32>>;
@group(0) @binding(5) var<uniform> bucket : Bucket;

const VERTEX_FLOATS : u32 = 11u;
const F_HIDDEN : u32 = 8u;
const F_NO_GI : u32 = 32u;
//Per invocation, not per triangle: gid.z selects which batch of this many an invocation
//splats, so a triangle spanning thousands of cells is spread over threads rather than looped
//serially in one. See SAMPLES_PER_INVOCATION in core/VoxelGrid.
const SAMPLES_PER_INVOCATION : u32 = 64u;

fn position(v : u32) -> vec3f {
  let at = v * VERTEX_FLOATS;
  return vec3f(vertices[at], vertices[at + 1u], vertices[at + 2u]);
}

//Radical inverse base 2. Stratifies the splat so a large triangle covers its cells evenly
//instead of clumping, which a plain uniform random would not guarantee at 64 samples.
fn radicalInverse2(bits : u32) -> f32 {
  var b = bits;
  b = (b << 16u) | (b >> 16u);
  b = ((b & 0x55555555u) << 1u) | ((b & 0xAAAAAAAAu) >> 1u);
  b = ((b & 0x33333333u) << 2u) | ((b & 0xCCCCCCCCu) >> 2u);
  b = ((b & 0x0F0F0F0Fu) << 4u) | ((b & 0xF0F0F0F0u) >> 4u);
  b = ((b & 0x00FF00FFu) << 8u) | ((b & 0xFF00FF00u) >> 8u);
  return f32(b) * 2.3283064365386963e-10;
}

//Rounds rather than truncates. Truncation is a one-sided error, and with tens of thousands of
//deposits per frame the bias accumulates into a measurable shortfall in total area.
fn deposit(base : u32, area : f32, color : vec3f) {
  let a = u32(round(area * AREA_SCALE));
  if (a == 0u) { return; }
  atomicAdd(&field[base], a);
  atomicAdd(&field[base + 1u], u32(round(area * color.r * AREA_SCALE)));
  atomicAdd(&field[base + 2u], u32(round(area * color.g * AREA_SCALE)));
  atomicAdd(&field[base + 3u], u32(round(area * color.b * AREA_SCALE)));
}

@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) gid : vec3u) {
  if (gid.x >= bucket.slice.y || gid.y >= bucket.counts.x) { return; }

  let inst = instances[bucket.slice.w + gid.y];
  //Hidden instances are excluded from drawing and picking, so they must not occlude either -
  //otherwise hiding the leaves would still leave their shadow in the light field. Terrain
  //carries F_NO_GI for a different reason; see IFlags.ExcludeFromGI.
  if ((inst.flags & (F_HIDDEN | F_NO_GI)) != 0u) { return; }

  let i0 = bucket.slice.x + gid.x * 3u;
  let v0 = bucket.slice.z + indices[i0];
  let v1 = bucket.slice.z + indices[i0 + 1u];
  let v2 = bucket.slice.z + indices[i0 + 2u];

  let a = (inst.model * vec4f(position(v0), 1.0)).xyz;
  let b = (inst.model * vec4f(position(v1), 1.0)).xyz;
  let c = (inst.model * vec4f(position(v2), 1.0)).xyz;

  //Area in world space, after the model transform: a leaf's contribution depends on how big
  //it actually is, not on how big its prototype mesh was.
  let area = 0.5 * length(cross(b - a, c - a));
  if (area <= 0.0) { return; }

  //One sample for a leaf triangle, up to 64 for a terrain quad. Scaling with area/cellArea
  //is what makes those one rule: without it a big quad dumps all its area into whichever
  //cell holds its centroid.
  let cellArea = grid.cell.x * grid.cell.z;
  let budget = SAMPLES_PER_INVOCATION * bucket.counts.y;
  let n = clamp(u32(ceil(area / cellArea * 4.0)), 1u, budget);
  //This invocation owns one slice of the samples. Batches past what the triangle needs have
  //nothing to do, which is what makes an over-provisioned batch count merely wasteful.
  let first = gid.z * SAMPLES_PER_INVOCATION;
  if (first >= n) { return; }
  let last = min(n, first + SAMPLES_PER_INVOCATION);
  let share = area / f32(n);
  //Emissive carries the data-mapping ramps; albedo is what bleeds. Both are already linear.
  let color = inst.color.rgb + inst.emissive.rgb;

  for (var i = first; i < last; i++) {
    let u = (f32(i) + 0.5) / f32(n);
    let v = radicalInverse2(i);
    let su = sqrt(u);
    let p = a * (1.0 - su) + b * (su * (1.0 - v)) + c * (su * v);

    let cell = cellOf(grid, p);
    if (!insideGrid(grid, cell)) { continue; }
    deposit(cellIndex(grid, cell) * 4u, share, color);
  }
}
`;

/**
 * Resolve: fixed-point area buffer to a filterable 3D texture.
 *
 * Stores sigma-premultiplied radiance (`rgb = sigma * albedo`, `a = sigma`). That layout is
 * what makes box downsampling exactly correct for both channels at once, and removes the
 * alpha-mip scale inconsistency that plagues opacity-based voxel GI.
 */
export const VOXEL_RESOLVE_WGSL = /* wgsl */`${VOXEL_COMMON}

//Lighting for the voxels themselves, so a cell has radiance to bleed rather than just albedo.
struct VoxelLight {
  //xyz: direction TO the sun, world space. w: 1 when the shadow map is valid.
  sunDir    : vec4f,
  //rgb: sun colour, already scaled by intensity.
  sunColor  : vec4f,
  //rgb: hemisphere sky. w: unused.
  sky       : vec4f,
  //rgb: hemisphere ground. w: shadow texel size.
  ground    : vec4f,
  lightViewProj : mat4x4f,
};

@group(0) @binding(0) var<uniform> grid : Grid;
@group(0) @binding(1) var<storage, read> field : array<u32>;
//Named dst because target is a reserved keyword in WGSL, and Tint rejects the whole module.
@group(0) @binding(2) var dst : texture_storage_3d<rgba16float, write>;
@group(0) @binding(3) var<uniform> light : VoxelLight;
@group(0) @binding(4) var shadowMap : texture_depth_2d;
@group(0) @binding(5) var shadowSampler : sampler_comparison;
//Compaction. A 128 grid is two million cells and a canopy occupies a few thousand of them, so
//the second bounce dispatches over this list rather than over the volume - the difference
//between 32768 workgroups and a handful.
@group(0) @binding(6) var<storage, read_write> occupied : array<u32>;
@group(0) @binding(7) var<storage, read_write> occupiedCount : atomic<u32>;

//sigma = G * LAD, with G = 0.5 for a spherical leaf-angle distribution: averaged over all
//orientations a leaf projects half its area onto any given direction.
const EXTINCTION_G : f32 = 0.5;

//Whether the sun contributes to a cell at all.
//
//No shadow lookup: the shadow map is gone, and a cell cannot sample the very field it is being
//resolved into. Cells are therefore lit as if unoccluded, which slightly overestimates the
//bounce deep inside a dense canopy - the cone trace still attenuates whatever reaches the eye,
//and paying for a 4096 depth pass to refine a second-order term is not a good trade.
fn voxelSunlight(p : vec3f) -> f32 {
  return select(0.0, 1.0, light.sunDir.w >= 0.5);
}

@compute @workgroup_size(4, 4, 4)
fn main(@builtin(global_invocation_id) gid : vec3u) {
  let d = vec3u(grid.dim.xyz);
  if (gid.x >= d.x || gid.y >= d.y || gid.z >= d.z) { return; }

  let cell = (gid.z * d.y + gid.y) * d.x + gid.x;
  let base = cell * 4u;
  let area = f32(field[base]) / AREA_SCALE;
  if (area <= 0.0) {
    textureStore(dst, vec3i(gid), vec4f(0.0));
    return;
  }

  //Record the cell while we already know it is occupied. Guarded on capacity rather than
  //assumed: the list is sized for the whole grid, but a clamp is cheaper than a corrupt write.
  let slot = atomicAdd(&occupiedCount, 1u);
  if (slot < arrayLength(&occupied)) { occupied[slot] = cell; }

  //grid.cell.w is 1/cellVolume, so this is LAD in m2/m3.
  let sigma = EXTINCTION_G * area * grid.cell.w;
  let albedo = vec3f(f32(field[base + 1u]), f32(field[base + 2u]), f32(field[base + 3u]))
             / (AREA_SCALE * area);

  //Light the cell. A voxel has no normal - it is a density, not a surface - so the sun term
  //uses the spherical average of |cos| over all leaf orientations, which is exactly the G of
  //0.5 the extinction already assumes. Using a normal here would be inventing one.
  let centre = grid.origin.xyz + (vec3f(gid) + vec3f(0.5)) * grid.cell.xyz;
  let ambient = mix(light.ground.rgb, light.sky.rgb, 0.5);
  let direct = light.sunColor.rgb * EXTINCTION_G * voxelSunlight(centre);
  let radiance = albedo * (ambient + direct);

  //Stored sigma-premultiplied, which is what makes a box downsample exactly correct for both
  //the radiance and the density at once.
  textureStore(dst, vec3i(gid), vec4f(sigma * radiance, sigma));
}
`;

/**
 * Mip generation.
 *
 * A plain arithmetic mean, which is only correct because the field stores a DENSITY and a
 * density-premultiplied radiance. Had it stored opacity, averaging would be wrong at every
 * level and the cone trace would darken with distance for no physical reason.
 */
export const VOXEL_MIP_WGSL = /* wgsl */`
@group(0) @binding(0) var src : texture_3d<f32>;
@group(0) @binding(1) var dst : texture_storage_3d<rgba16float, write>;

@compute @workgroup_size(4, 4, 4)
fn main(@builtin(global_invocation_id) gid : vec3u) {
  let size = textureDimensions(dst);
  if (gid.x >= size.x || gid.y >= size.y || gid.z >= size.z) { return; }

  //Divide by the number of texels that actually exist, not by 8.
  //
  //Once the grid stopped being a cube its axes stop halving at different levels: at 240x80x80
  //the short axes reach 1 while the long one still has 2 to go, and from there the source is
  //one texel thick. textureLoad returns zero out of bounds, so a fixed 0.125 would average in
  //phantom empty cells and bias the coarse levels low - which is exactly the levels a far cone
  //step reads, so a distant building would quietly thin out.
  let srcSize = vec3i(textureDimensions(src, 0));
  let base = vec3i(gid) * 2;
  var sum = vec4f(0.0);
  var n = 0.0;
  for (var z = 0; z < 2; z++) {
    for (var y = 0; y < 2; y++) {
      for (var x = 0; x < 2; x++) {
        let at = base + vec3i(x, y, z);
        if (all(at < srcSize)) {
          sum += textureLoad(src, at, 0);
          n += 1.0;
        }
      }
    }
  }
  textureStore(dst, vec3i(gid), select(vec4f(0.0), sum / n, n > 0.0));
}
`;

/**
 * Turns the occupied-cell count into indirect dispatch arguments.
 *
 * One invocation, because the alternative is reading the count back to the CPU, which means a
 * round trip and a stall in the middle of building a frame.
 */
export const VOXEL_ARGS_WGSL = /* wgsl */`
@group(0) @binding(0) var<storage, read> occupiedCount : array<u32>;
@group(0) @binding(1) var<storage, read_write> args : array<u32>;

@compute @workgroup_size(1)
fn main() {
  let n = occupiedCount[0];
  args[0] = (n + 63u) / 64u;
  args[1] = 1u;
  args[2] = 1u;
}
`;

/**
 * The second bounce.
 *
 * Each occupied cell gathers light from the rest of the field and re-emits it. That is what
 * lifts a canopy interior: with one bounce a leaf deep inside the crown sees only sky it
 * cannot reach, so it goes dark; with two it sees the leaves around it, which are lit.
 *
 * Reads the first-bounce texture and writes a second, because a pass cannot sample the
 * storage texture it writes. The destination is seeded with a copy of the source, so cells
 * this pass does not visit - the empty majority - keep their values.
 */
export const VOXEL_BOUNCE_WGSL = /* wgsl */`${VOXEL_COMMON}

@group(0) @binding(0) var<uniform> grid : Grid;
@group(0) @binding(1) var<storage, read> field : array<u32>;
@group(0) @binding(2) var<storage, read> occupied : array<u32>;
@group(0) @binding(3) var<storage, read> occupiedCount : array<u32>;
@group(0) @binding(4) var src : texture_3d<f32>;
@group(0) @binding(5) var srcSampler : sampler;
@group(0) @binding(6) var dst : texture_storage_3d<rgba16float, write>;

const EXTINCTION_G : f32 = 0.5;

//Six axis-aligned cones. A cell has no normal - it is a density - so the gather is spherical
//rather than hemispherical, and the six directions are the cheapest arrangement that covers
//it without favouring one axis.
const DIRS : array<vec3f, 6> = array<vec3f, 6>(
  vec3f( 1.0, 0.0, 0.0), vec3f(-1.0, 0.0, 0.0),
  vec3f( 0.0, 1.0, 0.0), vec3f( 0.0,-1.0, 0.0),
  vec3f( 0.0, 0.0, 1.0), vec3f( 0.0, 0.0,-1.0),
);

fn gatherCone(origin : vec3f, dir : vec3f) -> vec3f {
  let cellSize = min(grid.cell.x, min(grid.cell.y, grid.cell.z));
  let extent = grid.cell.xyz * grid.dim.xyz;
  let lo = grid.origin.xyz;

  var radiance = vec3f(0.0);
  var transmittance = 1.0;
  //Start clear of the cell doing the gathering, or it mostly re-reads itself.
  var dist = cellSize * 1.5;

  for (var i = 0; i < 8; i++) {
    if (dist >= 2.0 || transmittance < 0.05) { break; }
    let diameter = max(cellSize, 1.15 * dist);
    let mip = clamp(log2(diameter / cellSize), 0.0, 6.0);
    let p = origin + dir * dist;
    let uvw = (p - lo) / extent;
    if (any(uvw < vec3f(0.0)) || any(uvw > vec3f(1.0))) { break; }

    let texel = textureSampleLevel(src, srcSampler, uvw, mip);
    if (texel.a > 0.0) {
      let absorbed = 1.0 - exp(-texel.a * diameter);
      radiance += transmittance * absorbed * (texel.rgb / texel.a);
      transmittance *= 1.0 - absorbed;
    }
    dist += max(cellSize, diameter) * 0.5;
  }
  return radiance;
}

@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) gid : vec3u) {
  if (gid.x >= occupiedCount[0] || gid.x >= arrayLength(&occupied)) { return; }

  let cell = occupied[gid.x];
  let d = vec3u(grid.dim.xyz);
  let x = cell % d.x;
  let y = (cell / d.x) % d.y;
  let z = cell / (d.x * d.y);

  let base = cell * 4u;
  let area = f32(field[base]) / AREA_SCALE;
  if (area <= 0.0) { return; }
  let albedo = vec3f(f32(field[base + 1u]), f32(field[base + 2u]), f32(field[base + 3u]))
             / (AREA_SCALE * area);

  let centre = grid.origin.xyz + (vec3f(f32(x), f32(y), f32(z)) + vec3f(0.5)) * grid.cell.xyz;
  var gathered = vec3f(0.0);
  for (var i = 0; i < 6; i++) {
    gathered += gatherCone(centre, DIRS[i]);
  }
  //Averaged over the sphere, then halved by the same spherical G the extinction assumes: a
  //leaf presents half its area to any direction, and it re-emits what it catches.
  gathered = gathered * (1.0 / 6.0) * EXTINCTION_G;

  //Add to what the first bounce already produced rather than replacing it. Strictly additive,
  //so a second bounce can only ever brighten - which is the property that makes it safe to
  //leave on and the one worth asserting.
  let existing = textureLoad(src, vec3i(i32(x), i32(y), i32(z)), 0);
  let sigma = existing.a;
  if (sigma <= 0.0) { return; }
  textureStore(dst, vec3i(i32(x), i32(y), i32(z)),
               vec4f(existing.rgb + sigma * albedo * gathered, sigma));
}
`;
