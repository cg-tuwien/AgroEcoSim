//The sky background: one fullscreen triangle sampling the CPU-built sky cube by view ray.
//
//There is no sky model in here on purpose. core/SkyTexture fills six faces on the CPU and BOTH
//backends sample those same bytes - the Three backend hands them to three's built-in
//background shader, this one samples them directly - so the two cannot drift. Writing the CIE
//formula a second time in WGSL is exactly the drift this avoids.

export const SKY_WGSL = /* wgsl */`
struct Sky {
  //Ray basis, already scaled by tan(fovY/2) and aspect, so a pixel's direction is
  //right*ndc.x + up*ndc.y + forward. Packed as gi/VoxelDebug packs its own, minus the eye:
  //a background is infinitely far away, so the camera POSITION does not enter into it.
  right   : vec4f,
  up      : vec4f,
  forward : vec4f,
};

@group(0) @binding(0) var<uniform> sky : Sky;
@group(0) @binding(1) var skyMap : texture_cube<f32>;
@group(0) @binding(2) var skySampler : sampler;

struct VSOut {
  @builtin(position) position : vec4f,
  @location(0) ndc : vec2f,
};

@vertex
fn vs(@builtin(vertex_index) vi : u32) -> VSOut {
  //One oversized triangle rather than a quad: no diagonal seam, one fewer vertex.
  var pts = array<vec2f, 3>(vec2f(-1.0, -3.0), vec2f(-1.0, 1.0), vec2f(3.0, 1.0));
  var o : VSOut;
  //Depth is reversed here (near = 1, far = 0) but the pass neither tests nor writes it - the
  //sky is drawn first and everything else covers it - so this only has to land inside the
  //clip volume.
  o.position = vec4f(pts[vi], 0.0, 1.0);
  o.ndc = pts[vi];
  return o;
}

//Literal port of three r152's LinearTosRGB, duplicated from mesh.wgsl because a shader module
//is a standalone string and WGSL has no include. The exponent really is the truncated 0.41666.
fn linearToSRGB(c : vec3f) -> vec3f {
  return select(pow(c, vec3f(0.41666)) * 1.055 - vec3f(0.055),
                c * 12.92,
                c <= vec3f(0.0031308));
}

@fragment
fn fs(i : VSOut) -> @location(0) vec4f {
  let dir = normalize(sky.right.xyz * i.ndc.x + sky.up.xyz * i.ndc.y + sky.forward.xyz);
  //X is negated because the cube is stored in three's env-map space: WebGLBackground samples a
  //non-render-target CubeTexture with flipEnvMap = -1, so the CPU builder wrote the faces
  //mirrored. Undoing it here is what makes both backends read the same texel for the same world
  //direction. Drop it and east and west swap - and the sun rises in the west.
  let c = textureSampleLevel(skyMap, skySampler, vec3f(-dir.x, dir.y, dir.z), 0.0);
  //The cube is an -srgb format, so the sample came back LINEAR and was filtered in linear
  //space, matching what WebGL does with an SRGB8_ALPHA8 texture. The canvas is deliberately
  //NOT an sRGB format and takes the bytes verbatim, so re-encode here.
  return vec4f(linearToSRGB(c.rgb), 1.0);
}
`;
