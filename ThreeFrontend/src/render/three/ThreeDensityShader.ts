//GLSL for sampling a leaf-area-density field, injected into three's standard material.
//
//This is the WebGL half of the same idea the WebGPU backend uses: instead of a shadow map,
//integrate extinction along the ray to the sun and apply Beer-Lambert. The reason for going to
//this trouble rather than tuning the shadow map is structural, not cosmetic -
//
//A leaf is a zero-thickness double-sided quad. Its front and back faces are at exactly the same
//depth, so when it is rendered into the shadow map it records its OWN depth, and the comparison
//that follows is a tie. Whether a fragment lands on the lit or the shadowed side of that tie
//then depends on rasterisation and interpolation rounding, which is why the artifact looks like
//a leaf shadowing itself in a torn, triangle-shaped pattern. Depth bias cannot fix it: the
//required offset is proportional to the depth slope across a texel, which for a leaf turned
//edge-on to the sun grows without bound. That is what "bias does not help" means.
//
//Marching a density field has no such tie. The march starts two cells clear of the surface, so
//a surface can never occlude itself, and the result is a smooth transmittance rather than a
//binary test - which is also what foliage physically does, since leaves transmit light.

/** Uniform declarations and the two lookups. Injected into the fragment shader's preamble. */
export const DENSITY_FRAGMENT_HEAD = /* glsl */`
uniform highp sampler3D uDensityTex;
uniform vec3 uDensityOrigin;
uniform vec3 uDensityInvSize;
uniform vec3 uDensitySunDir;
uniform float uDensityCell;
uniform float uDensitySun;
uniform float uDensityAo;
varying vec3 vDensityWorld;
varying vec3 vDensityNormal;

float densityAt(vec3 p) {
    vec3 uvw = (p - uDensityOrigin) * uDensityInvSize;
    //Outside the grid there is nothing to occlude with. Clamping instead would smear the
    //border cells out to infinity and put a shadow under every plant at the field edge.
    if (any(lessThan(uvw, vec3(0.0))) || any(greaterThan(uvw, vec3(1.0)))) return 0.0;
    return texture(uDensityTex, uvw).r;
}

//Beer-Lambert transmittance towards the sun.
float densitySunTransmittance(vec3 p) {
    if (uDensitySun <= 0.0) return 1.0;

    //Two cells of clearance. This is the whole reason a surface cannot shadow itself here, and
    //it is the same offset the WebGPU cone tracer uses, so the two backends agree.
    vec3 pos = p + uDensitySunDir * (uDensityCell * 2.0);
    float tau = 0.0;

    //Steps grow geometrically: near the surface detail matters, far away only the total does.
    //24 uniform steps would reach barely four metres at this cell size, which is not enough to
    //clear a canopy from a low sun.
    float step = uDensityCell;
    for (int i = 0; i < 24; ++i) {
        tau += densityAt(pos) * step;
        pos += uDensitySunDir * step;
        step *= 1.12;
    }

    return mix(1.0, exp(-tau), uDensitySun);
}

//Ambient occlusion: how much of the sky this point can actually see.
//
//This is a CONTACT-scale effect and it has to be marched differently from the sun. The first
//attempt reused the sun\'s two-cell start offset and produced a change of 4/255 - measurable but
//invisible. At the grid resolution then in use two cells was 0.33 m, and the plant being shaded
//is 0.30 m across, so every AO ray began beyond the whole plant and sampled empty air. The sun
//does not care, because it marches metres and meets the canopy above; occlusion cares about the
//leaf next to this one.
//
//So: a short start offset, a short reach, and a cone set that actually spreads.
float densityOcclusion(vec3 p, vec3 n) {
    if (uDensityAo <= 0.0) return 1.0;

    //One along the normal plus four at 45 degrees, evenly spaced in azimuth. The previous set
    //had vec3(0.0, 0.258819, 0.0) as its fifth direction, which normalises straight back to the
    //normal - so it was four cones pretending to be five, with a narrower spread than intended.
    const vec3 CONES[5] = vec3[5](
        vec3( 0.0,      1.0,      0.0),
        vec3( 0.707107, 0.707107, 0.0),
        vec3(-0.707107, 0.707107, 0.0),
        vec3( 0.0,      0.707107, 0.707107),
        vec3( 0.0,      0.707107,-0.707107));
    //Cosine weights: a direction at 45 degrees to the normal contributes less irradiance than
    //one along it, so it should count for less when deciding how much sky is blocked.
    const float WEIGHTS[5] = float[5](1.0, 0.707107, 0.707107, 0.707107, 0.707107);
    const float TOTAL_WEIGHT = 3.828428;

    //An orthonormal frame around the normal, so the cone set follows the surface.
    vec3 up = abs(n.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    vec3 t = normalize(cross(up, n));
    vec3 b = cross(n, t);

    float occluded = 0.0;
    for (int c = 0; c < 5; ++c) {
        vec3 dir = normalize(t * CONES[c].x + n * CONES[c].y + b * CONES[c].z);
        //Three quarters of a cell of clearance - enough that a surface is not occluded by the
        //cell it sits in, short enough that the neighbouring leaf still counts.
        vec3 pos = p + dir * (uDensityCell * 0.75);
        float tau = 0.0;
        float step = uDensityCell * 0.75;
        //Six steps at 1.15x reach about nine cells, which is the scale of a plant rather than
        //of the field. Occlusion beyond that is the sky being blocked by a different plant, and
        //that is the shadow term\'s job.
        for (int i = 0; i < 6; ++i) {
            tau += densityAt(pos) * step;
            pos += dir * step;
            step *= 1.15;
        }
        occluded += WEIGHTS[c] * (1.0 - exp(-tau));
    }
    occluded /= TOTAL_WEIGHT;

    //0.85 rather than 1.0: a fully enclosed point keeps a little ambient, which is both closer
    //to reality and stops the canopy interior going flat black.
    return mix(1.0, 1.0 - occluded * 0.85, uDensityAo);
}
`;

/**
 * Applied immediately after `lights_fragment_begin`, which is the one point in three's chain
 * where both quantities are available and neither has been consumed yet: `RE_Direct` has
 * already accumulated the sun into `reflectedLight.directDiffuse`, while `irradiance` still
 * holds the hemisphere term that `lights_fragment_end` will hand to `RE_IndirectDiffuse`.
 *
 * Scaling them here rather than replacing three's light loop means every other material feature
 * keeps working, and with both uniforms at zero the arithmetic is a multiply by exactly 1.0 -
 * so the default tier stays bit-identical to what it rendered before.
 */
export const DENSITY_FRAGMENT_APPLY = /* glsl */`
    reflectedLight.directDiffuse *= densitySunTransmittance(vDensityWorld);
    irradiance *= densityOcclusion(vDensityWorld, normalize(vDensityNormal));
`;

export const DENSITY_VERTEX_HEAD = /* glsl */`
varying vec3 vDensityWorld;
varying vec3 vDensityNormal;
`;

/**
 * Applied after `project_vertex`, where `transformed` is the final object-space position.
 *
 * The normal is the smooth object normal rather than the flat-shaded one the fragment shader
 * derives: occlusion is a hemispherical average and does not want faceting, and taking it here
 * avoids needing the inverse view matrix in the fragment shader.
 */
export const DENSITY_VERTEX_APPLY = /* glsl */`
    vDensityWorld = (modelMatrix * vec4(transformed, 1.0)).xyz;
    vDensityNormal = normalize(mat3(modelMatrix) * objectNormal);
`;
