//WGSL for the main scene pass and the picking pass.
//
//Shipped as a template literal because preact.config.js is entirely commented out, so there
//is no loader for .wgsl files and adding one would mean re-enabling the whole webpack config
//for a cosmetic gain. The /* wgsl */ prefix makes editors highlight it, and WGSL contains
//neither backticks nor ${ so composition is collision-free - the same approach three uses
//for ShaderChunk.

export const SHARED = /* wgsl */`
struct Frame {
  viewProj  : mat4x4f,
  view      : mat4x4f,
  //World -> light clip, for the shadow lookup.
  lightViewProj : mat4x4f,
  //Sky and ground already include the light intensity. three multiplies them by PI for
  //useLegacyLights and BRDF_Lambert then multiplies by 1/PI, so both factors are omitted
  //here: the product is identical and the shader is simpler.
  hemiSky   : vec4f,
  hemiGround: vec4f,
  //Light direction in VIEW space, matching three's uniforms.direction which is the light's
  //world position transformed by the view matrix and normalized.
  hemiDir   : vec4f,
  //Direction TO the sun in VIEW space, to match the hemisphere term's space.
  sunDirView : vec4f,
  //Already multiplied by intensity. Black at Tier 0, which is what makes the sun term
  //vanish rather than needing a separate shading path.
  sunColor  : vec4f,
  //x: unused (the shadow map is gone; see sunVisibility), y: unused,
  //z: 1 when ambient occlusion is available, w: 1 when the cone trace ran.
  params    : vec4f,
  //xy: tan(fovY/2) scaled by aspect and 1, for rebuilding a view position from depth.
  //zw: the AO target's dimensions.
  projParams : vec4f,
  //Direction TO the sun in WORLD space. The shadow lookup needs it there, not in view space,
  //because the offset it applies is along a world-space surface normal.
  sunDirWorld : vec4f,
  //x: one shadow texel in WORLD units. y: constant offset in texels. z: extra texels at
  //grazing incidence. w: depth bias in NORMALISED depth units.
  shadowParams : vec4f,

};

struct Instance {
  model     : mat4x4f,
  //Cofactor of the model's upper 3x3, i.e. the inverse-transpose up to a scale factor that
  //normalization cancels.
  n0        : vec4f,
  n1        : vec4f,
  n2        : vec4f,
  color     : vec4f,
  emissive  : vec4f,
  pickId    : u32,
  flags     : u32,
  _pad0     : u32,
  _pad1     : u32,
};

//Fraction of incident sunlight a leaf passes through to its shaded face. Foliage transmits
//roughly this much in the visible band, and rather more in green; a single scalar keeps the
//light filtered once by the leaf's own colour, which is close enough at this scale.
const LEAF_TRANSMISSION : f32 = 0.4;

//Wrap-around diffuse, the cheap stand-in for subsurface scattering in a thin translucent
//surface. Light entering a leaf scatters inside it before leaving, so the falloff towards the
//terminator is far softer than a bare cosine - and a bare cosine is what made a leaf's own
//triangles read as separate facets, because near dot(n, L) == 0 the cosine changes fastest and
//it goes to zero on BOTH sides, leaving a dark crease straight through the leaf.
//
//Wrapping shifts the falloff so nothing reaches zero at the terminator and the response to a
//normal is compressed, which is exactly the visual difference between a leaf and a sheet of
//metal. It is not a full SSS model - no thickness, no lateral bleed - but for something one
//cell thick it is most of the effect.
const LEAF_WRAP : f32 = 0.7;

//Oren-Nayar roughness, as sigma squared, for the leaf's reflection.
//
//A Lambertian surface is smooth at the microscopic scale, and a leaf is not: Geranium and
//Heuchera carry fine hairs, which scatter light back towards the viewer and flatten the
//falloff further. Oren-Nayar's qualitative effect - a surface that stays bright away from the
//light rather than rolling off as a cosine - is what the eye reads as "matte and slightly
//dusty" instead of "moulded plastic". The A/B form below is the cheap approximation, with the
//view direction dropped: at this scale the difference is not worth the dot product.
const LEAF_ROUGHNESS : f32 = 0.6;

const F_DOUBLE : u32 = 1u;
const F_FLAT   : u32 = 2u;
const F_VCOL   : u32 = 4u;

@group(0) @binding(0) var<uniform> frame : Frame;
@group(1) @binding(0) var<storage, read> instances : array<Instance>;

struct VSOut {
  @builtin(position) position : vec4f,
  @location(6) worldPos : vec3f,
  //World-space normal, for the shadow lookup's normal offset. The shading normal cannot be
  //reused: it is in view space, and flat-shaded surfaces derive it from screen derivatives.
  @location(7) worldNrm : vec3f,
  @location(0) viewPos  : vec3f,
  @location(1) nrmView  : vec3f,
  @location(2) color    : vec3f,
  @location(3) emissive : vec3f,
  @location(4) @interpolate(flat) flags  : u32,
  @location(5) @interpolate(flat) pickId : u32,
};

@vertex
fn vs(
  @location(0) pos  : vec3f,
  @location(1) nrm  : vec3f,
  @location(2) uv   : vec2f,
  @location(3) vcol : vec3f,
  @builtin(instance_index) ii : u32,
) -> VSOut {
  //instance_index is absolute: it already includes firstInstance, so one storage buffer and
  //one bind group serve every bucket.
  let inst = instances[ii];

  let world = inst.model * vec4f(pos, 1.0);
  let viewPos4 = frame.view * world;

  //three computes normalMatrix as (V*M)^-T. V is rigid here, so V^-T == mat3(V) and the
  //model part can be precomputed on the CPU.
  let nm = mat3x3f(inst.n0.xyz, inst.n1.xyz, inst.n2.xyz);
  let v3 = mat3x3f(frame.view[0].xyz, frame.view[1].xyz, frame.view[2].xyz);

  var out : VSOut;
  out.position = frame.viewProj * world;
  out.worldPos = world.xyz;
  //The cofactor rows are the inverse-transpose up to a scale that normalization cancels.
  out.worldNrm = normalize(vec3f(dot(inst.n0.xyz, nrm), dot(inst.n1.xyz, nrm), dot(inst.n2.xyz, nrm)));
  out.viewPos  = viewPos4.xyz;
  out.nrmView  = v3 * (nm * nrm);
  out.flags    = inst.flags;
  out.pickId   = inst.pickId;
  out.color    = select(inst.color.rgb, vcol, (inst.flags & F_VCOL) != 0u);
  out.emissive = inst.emissive.rgb;
  return out;
}

//Literal port of three r152's LinearTosRGB. The exponent really is the truncated 0.41666
//and not 1/2.4; using the exact value shifts low-end gradients by a bit.
fn linearToSRGB(c : vec3f) -> vec3f {
  return select(pow(c, vec3f(0.41666)) * 1.055 - vec3f(0.055),
                c * 12.92,
                c <= vec3f(0.0031308));
}

fn shadingNormal(i : VSOut, frontFacing : bool) -> vec3f {
  //Derivatives must not sit behind non-uniform control flow, so the flat normal is always
  //computed. three's vViewPosition is -mvPosition, but cross(-a,-b) == cross(a,b), so that
  //sign cancels.
  //
  //The operand ORDER is swapped relative to the GLSL version on purpose. WebGPU's framebuffer
  //origin is top-left and GL's is bottom-left, so dpdy has the opposite sign here and
  //cross(dpdx, dpdy) would point the wrong way - which shows up as flat-shaded surfaces
  //(terrain, obstacles) lit by the ground colour instead of the sky.
  let flatN = normalize(cross(dpdy(i.viewPos), dpdx(i.viewPos)));
  //FLAT_SHADED wins in three and deliberately does NOT apply faceDirection.
  let doubleSided = (i.flags & F_DOUBLE) != 0u;
  let facing = select(-1.0, 1.0, frontFacing || !doubleSided);
  let smoothN = normalize(i.nrmView) * facing;
  return select(smoothN, flatN, (i.flags & F_FLAT) != 0u);
}
`;

/**
 * The shadow map's bindings, kept apart from SHARED on purpose.
 *
 * The pass that FILLS the shadow map must not also declare it as readable: WebGPU forbids a
 * texture being bound and used as a render attachment within one synchronisation scope, and
 * Dawn rejects the whole command buffer if it is. Only the scene shader includes this.
 */
export const SHADOW_SAMPLING = /* wgsl */`
@group(0) @binding(1) var shadowMap : texture_depth_2d;
@group(0) @binding(2) var shadowSampler : sampler_comparison;
@group(0) @binding(3) var aoMap : texture_2d<f32>;
@group(0) @binding(4) var aoSampler : sampler;
@group(0) @binding(5) var giMap : texture_2d<f32>;

/**
 * Ambient occlusion at this fragment.
 *
 * Sampled by screen position, so it needs no extra interpolant. Returns 1 when occlusion is
 * disabled, which is what keeps the lower tiers untouched.
 */
fn ambientOcclusion(fragCoord : vec2f) -> f32 {
  if (frame.params.z < 0.5) { return 1.0; }
  let uv = fragCoord / frame.projParams.zw;
  return textureSampleLevel(aoMap, aoSampler, uv, 0.0).r;
}

//Cone-traced indirect light: rgb is bounced radiance, a is SUN TRANSMITTANCE.
//
//Returns the identity - no bounce, nothing occluded - when the tier is off OR when the grid
//held nothing. That is what makes the invariant exact rather than approximate: the composite
//below multiplies the hemisphere term by a and adds rgb, so a = 1 and rgb = 0 reduces it to
//precisely the expression the tier below evaluates.
fn indirectLight(fragCoord : vec2f) -> vec4f {
  if (frame.params.w < 0.5) { return vec4f(0.0, 0.0, 0.0, 1.0); }
  let uv = fragCoord / frame.projParams.zw;
  return textureSampleLevel(giMap, aoSampler, uv, 0.0);
}

/**
 * Percentage-closer filtering over a 3x3 neighbourhood.
 *
 * Returns 1 where the fragment is lit. Anything outside the shadow map's frustum is treated
 * as lit rather than shadowed, so geometry beyond the field is not plunged into darkness.
 */
/**
 * How much direct sun reaches this point, from the density field rather than a shadow map.
 *
 * Shadow maps are the wrong instrument for foliage. A leaf is a single zero-thickness quad, so
 * the caster and the receiver are the SAME triangle: the depth comparison is a tie, only a bias
 * can break it, and the leaf ends up wearing a leaf-shaped shadow of itself. Every bias that is
 * large enough to stop that is also large enough to leak light between leaves that genuinely
 * touch. There is no comparison here - the field says how much leaf area lies between this
 * point and the sun, and Beer-Lambert turns it into a fraction - so self-shadowing cannot
 * happen at all, and the softness is the canopy's, not an artifact.
 *
 * Falls back to fully lit when the trace has not run, so the sun term simply stays unshadowed
 * rather than the surface going dark.
 */
fn sunVisibility(traced : vec4f) -> f32 {
  if (frame.params.w < 0.5) { return 1.0; }
  return traced.a;
}

`;

export const MESH_WGSL = /* wgsl */`${SHARED}
${SHADOW_SAMPLING}

@fragment
fn fs(i : VSOut, @builtin(front_facing) frontFacing : bool) -> @location(0) vec4f {
  let n = shadingNormal(i, frontFacing);
  //getHemisphereLightIrradiance: mix(ground, sky, 0.5*dot(N,L)+0.5).
  let w = 0.5 * dot(n, frame.hemiDir.xyz) + 0.5;
  //Occlusion darkens the AMBIENT term only. Direct sunlight is already occluded by the
  //shadow map, and multiplying it again would double-count and crush the lit side.
  //The cone trace's visibility and the screen-space AO measure the same thing at different
  //scales, so take the darker rather than multiplying: multiplying would count the contact
  //shadow twice wherever both see the same occluder.
  let indirect = indirectLight(i.position.xy);
  //Occlusion of the SKY comes from the screen-space pass, which resolves finer than an 8 cm
  //voxel can. The cone trace's alpha is the sun now, not the sky.
  let occlusion = ambientOcclusion(i.position.xy);
  //The hemisphere term is ATTENUATED by visibility and the bounce is ADDED, rather than the
  //trace replacing the ambient with its own estimate. With an empty grid visibility is 1 and
  //the bounce is 0, so this is bit-for-bit the tier below.
  let irradiance = mix(frame.hemiGround.rgb, frame.hemiSky.rgb, w) * occlusion + indirect.rgb;

  //A leaf REFLECTS from its sunlit face and TRANSMITS a fraction through to the shaded one.
  //
  //This used to be abs(dot(n, L)), which handed the shaded face the full sun as though it were
  //reflecting - so every leaf in a canopy read the same brightness whichever way it faced, the
  //crown looked flat, and indirect light had nothing to add because nothing was dark enough to
  //notice a bounce. Real foliage passes something like a fifth to a quarter of what lands on
  //it, and that asymmetry is most of what makes a canopy look like a canopy.
  //
  //Still never zero on the shaded side, so the reason abs() was there - a canopy going black
  //in patches - does not come back.
  let raw = dot(n, frame.sunDirView.xyz);
  //Wrapped on both faces, so neither the lit side nor the shaded one collapses to zero at the
  //terminator and there is no crease where the two meet.
  let wrap = 1.0 + LEAF_WRAP;
  let front = clamp((raw + LEAF_WRAP) / wrap, 0.0, 1.0);
  let back = clamp((-raw + LEAF_WRAP) / wrap, 0.0, 1.0) * LEAF_TRANSMISSION;

  //Oren-Nayar retro-reflection on top of the wrapped diffuse: a hairy leaf keeps more of its
  //brightness as it turns away from the sun than a cosine allows.
  let s2 = LEAF_ROUGHNESS * LEAF_ROUGHNESS;
  let onA = 1.0 - 0.5 * s2 / (s2 + 0.33);
  let onB = 0.45 * s2 / (s2 + 0.09);
  let sinTheta = sqrt(clamp(1.0 - front * front, 0.0, 1.0));
  let leaf = front * (onA + onB * sinTheta) + back;

  let ndl = select(max(raw, 0.0), leaf, (i.flags & F_DOUBLE) != 0u);
  //sunColor is black at Tier 0, so this whole term disappears and the result is exactly the
  //hemisphere-only value the WebGL renderer produces.
  let sun = frame.sunColor.rgb * ndl * sunVisibility(indirect);

  let outgoing = i.color * (irradiance + sun) + i.emissive;
  //LinearToneMapping at exposure 1 is the identity in r152, so it is deliberately absent.
  return vec4f(linearToSRGB(outgoing), 1.0);
}
`;

export const PICK_WGSL = /* wgsl */`${SHARED}

@fragment
fn fs(i : VSOut) -> @location(0) u32 {
  return i.pickId;
}
`;


/**
 * Depth-only pass that fills the shadow map.
 *
 * It reuses the same vertex stage and instance buffer, so anything drawn in the scene casts
 * a shadow without a second description of the geometry.
 */
export const SHADOW_WGSL = /* wgsl */`${SHARED}

@fragment
fn fs(i : VSOut) {
  // Depth-only: nothing to write.
}
`;


/** Octahedral encoding: a unit vector in two components, with no seam artefacts in practice. */
export const OCT_ENCODE = /* wgsl */`
fn octEncode(n : vec3f) -> vec2f {
  let d = n / (abs(n.x) + abs(n.y) + abs(n.z));
  if (d.z >= 0.0) { return d.xy; }
  return (vec2f(1.0) - abs(d.yx)) * select(vec2f(-1.0), vec2f(1.0), d.xy >= vec2f(0.0));
}

fn octDecode(e : vec2f) -> vec3f {
  var n = vec3f(e.x, e.y, 1.0 - abs(e.x) - abs(e.y));
  if (n.z < 0.0) {
    let xy = (vec2f(1.0) - abs(n.yx)) * select(vec2f(-1.0), vec2f(1.0), n.xy >= vec2f(0.0));
    n = vec3f(xy, n.z);
  }
  return normalize(n);
}
`;

/**
 * Depth + normal prepass.
 *
 * Rendered at full resolution WITHOUT multisampling: resolving a normal buffer would average
 * directions across an edge and produce normals pointing nowhere, which shows up as a halo
 * of wrong occlusion around every leaf.
 */
export const PREPASS_WGSL = /* wgsl */`${SHARED}
${OCT_ENCODE}

struct PrepassOut {
  //Octahedral view normal, and linear view depth in metres.
  @location(0) normal : vec2f,
  @location(1) depth  : f32,
};

@fragment
fn fs(i : VSOut, @builtin(front_facing) frontFacing : bool) -> PrepassOut {
  let n = shadingNormal(i, frontFacing);
  //View space looks down -Z, so the distance in front of the camera is -viewPos.z.
  var o : PrepassOut;
  o.normal = octEncode(n);
  o.depth = -i.viewPos.z;
  return o;
}
`;

/**
 * Horizon-based ambient occlusion, at half resolution.
 *
 * Spatial only, with no temporal accumulation. A canopy's depth buffer is extreme
 * high-frequency detail and the geometry is replaced wholesale between timesteps, so
 * accumulating across frames would smear rather than converge - the same reason the GI
 * design rejects temporal methods for this scene.
 */
//Denoises the raw GTAO buffer. Depth-aware so occlusion never bleeds across a silhouette -
//an unweighted box blur would drag canopy darkening out over the background behind a leaf.
export const GTAO_BLUR_WGSL = /* wgsl */`
struct AoParams {
  projParams : vec4f,
  settings   : vec4f,
};

@group(0) @binding(0) var<uniform> ao : AoParams;
@group(0) @binding(1) var gDepth : texture_2d<f32>;
@group(0) @binding(2) var aoIn : texture_2d<f32>;
@group(0) @binding(3) var aoOut : texture_storage_2d<rgba8unorm, write>;

@compute @workgroup_size(8, 8)
fn main(@builtin(global_invocation_id) gid : vec3u) {
  let size = vec2i(i32(ao.projParams.z), i32(ao.projParams.w));
  if (gid.x >= u32(size.x) || gid.y >= u32(size.y)) { return; }

  let centreCoord = vec2i(gid.xy);
  let centreDist = textureLoad(gDepth, centreCoord * 2, 0).x;
  if (centreDist <= 0.0) {
    textureStore(aoOut, centreCoord, vec4f(1.0, 0.0, 0.0, 1.0));
    return;
  }

  //Tolerate depth differences proportional to distance, so the threshold holds up across
  //the whole 0.025..5000 range rather than only near the camera.
  let tolerance = max(0.05, centreDist * 0.02);

  var sum = 0.0;
  var weight = 0.0;
  for (var dy = -2; dy <= 1; dy++) {
    for (var dx = -2; dx <= 1; dx++) {
      let c = clamp(centreCoord + vec2i(dx, dy), vec2i(0, 0), size - vec2i(1, 1));
      let d = textureLoad(gDepth, c * 2, 0).x;
      if (d <= 0.0 || abs(d - centreDist) > tolerance) { continue; }
      sum += textureLoad(aoIn, c, 0).x;
      weight += 1.0;
    }
  }

  let visibility = select(textureLoad(aoIn, centreCoord, 0).x, sum / weight, weight > 0.0);
  textureStore(aoOut, centreCoord, vec4f(visibility, 0.0, 0.0, 1.0));
}
`;

export const GTAO_WGSL = /* wgsl */`${OCT_ENCODE}

struct AoParams {
  //xy: tan(fovY/2)*aspect, tan(fovY/2). zw: AO target size.
  projParams : vec4f,
  //x: radius in world units, y: intensity, zw: full-resolution size.
  settings   : vec4f,
};

@group(0) @binding(0) var<uniform> ao : AoParams;
@group(0) @binding(1) var gNormal : texture_2d<f32>;
@group(0) @binding(2) var gDepth : texture_2d<f32>;
@group(0) @binding(3) var aoOut : texture_storage_2d<rgba8unorm, write>;

const DIRECTIONS : u32 = 8u;
const STEPS : u32 = 6u;
const PI : f32 = 3.14159265;
const ANGLE_BIAS : f32 = 0.05;

fn viewPosFrom(uv : vec2f, dist : f32) -> vec3f {
  //uv has a top-left origin, clip space is y-up.
  let ndc = vec2f(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
  return vec3f(ndc.x * ao.projParams.x, ndc.y * ao.projParams.y, -1.0) * dist;
}

@compute @workgroup_size(8, 8)
fn main(@builtin(global_invocation_id) gid : vec3u) {
  let size = vec2u(u32(ao.projParams.z), u32(ao.projParams.w));
  if (gid.x >= size.x || gid.y >= size.y) { return; }

  let full = vec2i(i32(gid.x) * 2, i32(gid.y) * 2);
  let dist = textureLoad(gDepth, full, 0).x;

  //Nothing was drawn here, so there is nothing to occlude.
  if (dist <= 0.0) {
    textureStore(aoOut, vec2i(gid.xy), vec4f(1.0, 0.0, 0.0, 1.0));
    return;
  }

  let uv = (vec2f(f32(full.x), f32(full.y)) + 0.5) / ao.settings.zw;
  let p = viewPosFrom(uv, dist);
  let n = octDecode(textureLoad(gNormal, full, 0).xy);

  let radius = ao.settings.x;
  //Screen-space radius shrinks with distance, which is what keeps the effect world-scaled.
  let radiusPixels = clamp(radius / dist * ao.settings.w * 0.5, 4.0, 96.0);

  //Interleaved rotation over a 4x4 tile. The blur pass below averages exactly this tile, so
  //the 16 rotations integrate into an effective 128-direction estimate. Rotating over one
  //inter-direction gap (2PI/DIRECTIONS) rather than a full turn is what makes those 16
  //rotations sample distinct azimuths instead of repeating the same eight.
  let tile = (gid.y % 4u) * 4u + (gid.x % 4u);
  let rotation = f32(tile) / 16.0 * (PI * 2.0 / f32(DIRECTIONS));

  var occlusion = 0.0;
  for (var d = 0u; d < DIRECTIONS; d++) {
    let angle = rotation + f32(d) * (PI * 2.0 / f32(DIRECTIONS));
    let dir = vec2f(cos(angle), sin(angle));

    //Track the highest horizon seen along this direction, as HBAO does.
    var maxHorizon = 0.0;
    for (var s = 1u; s <= STEPS; s++) {
      let t = f32(s) / f32(STEPS);
      let offset = dir * radiusPixels * t;
      let sampleCoord = full + vec2i(i32(offset.x), i32(offset.y));
      if (sampleCoord.x < 0 || sampleCoord.y < 0
          || sampleCoord.x >= i32(ao.settings.z) || sampleCoord.y >= i32(ao.settings.w)) { continue; }

      let sampled = textureLoad(gDepth, sampleCoord, 0).x;
      if (sampled <= 0.0) { continue; }

      let sampleUv = (vec2f(f32(sampleCoord.x), f32(sampleCoord.y)) + 0.5) / ao.settings.zw;
      let delta = viewPosFrom(sampleUv, sampled) - p;
      let len = length(delta);
      if (len < 1e-5 || len > radius) { continue; }

      //Sine of the elevation above the tangent plane, faded out with distance so a far
      //occluder does not darken as much as a near one. The bias discards near-coplanar
      //samples: a max over ~48 samples turns any residual reconstruction error into a
      //one-sided positive horizon, which is exactly what self-shadows a flat surface.
      let horizon = (dot(n, delta / len) - ANGLE_BIAS) * (1.0 - len / radius);
      maxHorizon = max(maxHorizon, horizon);
    }
    occlusion += maxHorizon;
  }

  occlusion = occlusion / f32(DIRECTIONS);
  let visibility = clamp(1.0 - occlusion * ao.settings.y, 0.0, 1.0);
  textureStore(aoOut, vec2i(gid.xy), vec4f(visibility, 0.0, 0.0, 1.0));
}
`;
