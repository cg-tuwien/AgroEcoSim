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

import { LEAF_ROUGHNESS, LEAF_TRANSMISSION, LEAF_WRAP } from '../core/Lighting';

/** A number as a GLSL float literal. String(1) is "1", which is an int in GLSL. */
const f32lit = (v: number) => v.toFixed(4);

/** Uniform declarations and the two lookups. Injected into the fragment shader's preamble. */
export const DENSITY_FRAGMENT_HEAD = /* glsl */`
uniform highp sampler3D uDensityTex;
uniform vec3 uDensityOrigin;
uniform vec3 uDensityInvSize;
uniform vec3 uDensitySunDir;
uniform float uDensityCell;
uniform float uDensitySun;
uniform float uDensityAo;
//How much the brighter face of a lamina bleeds into the darker one, and whether this material
//is a lamina at all. Both are uniforms rather than defines: a define would recompile every
//material in the scene when the user ticks a box, and an instance slot is pooled, so the mesh
//that was a leaf last timestep can be a stem in this one.
uniform float uLeafTranslucency;
uniform float uLeafFoliage;
varying vec3 vDensityWorld;
varying vec3 vDensityNormal;

//The leaf constants come from core/Lighting, so this backend and the WGSL one cannot drift.
const float LEAF_WRAP = ${f32lit(LEAF_WRAP)};
const float LEAF_TRANSMISSION = ${f32lit(LEAF_TRANSMISSION)};
const float LEAF_ROUGHNESS = ${f32lit(LEAF_ROUGHNESS)};

//One face of a leaf's response to the sun, as a function of dot(n, L) for THAT face. A literal
//port of leafSun() in mesh.wgsl.ts; call it with -raw to get the other face.
//
//Wrapped, so neither face collapses to zero at the terminator - a bare cosine reaches zero on
//BOTH sides there, leaving a dark crease straight through any leaf whose triangles straddle it.
//Oren-Nayar on top, because a hairy leaf keeps more of its brightness as it turns away from the
//sun than a cosine allows. Plus the leaf's own transmission from the opposite face.
float leafSun(float raw) {
    float wrap = 1.0 + LEAF_WRAP;
    float front = clamp((raw + LEAF_WRAP) / wrap, 0.0, 1.0);
    float back = clamp((-raw + LEAF_WRAP) / wrap, 0.0, 1.0) * LEAF_TRANSMISSION;

    float s2 = LEAF_ROUGHNESS * LEAF_ROUGHNESS;
    float onA = 1.0 - 0.5 * s2 / (s2 + 0.33);
    float onB = 0.45 * s2 / (s2 + 0.09);
    float sinTheta = sqrt(clamp(1.0 - front * front, 0.0, 1.0));
    return front * (onA + onB * sinTheta) + back;
}

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
    //Leaf translucency: the brighter face of a lamina bleeds into the darker one, and the
    //brighter face itself is never touched. uLeafFoliage restricts it to leaves and petals;
    //uLeafTranslucency is zero unless the switch is on, and everything below is multiplied by
    //it, so with the switch off this block adds exactly zero and the default look is unchanged.
    //
    //It covers the AMBIENT and not just the sun, which is the whole point. three lights a
    //double-sided surface with the normal turned towards the VIEWER, so a leaf's underside gets
    //the hemisphere's GROUND colour and none of its sky - about a sixth of what its own topside
    //receives - and the default lighting has no sun at all for a transmission term to act on.
    float uLeafOn = step(1e-6, uLeafTranslucency) * uLeafFoliage;
    float uLeafT = uLeafTranslucency * uLeafFoliage;

    #if ( NUM_HEMI_LIGHTS > 0 )
        //Evaluated from the light itself rather than by mirroring irradiance, which also
        //carries the ambient light and the light probe. Only the bleed is added.
        vec3 hemiHere = getHemisphereLightIrradiance( hemisphereLights[ 0 ], geometry.normal );
        vec3 hemiThere = getHemisphereLightIrradiance( hemisphereLights[ 0 ], -geometry.normal );
        irradiance += uLeafT * max( hemiThere - hemiHere, vec3( 0.0 ) );
    #endif

    #if ( NUM_DIR_LIGHTS > 0 )
        //ADDED to what RE_Direct already accumulated rather than replacing it: reusing three's
        //own light colour and BRDF_Lambert is what makes the added term carry the same
        //useLegacyLights PI factor, and with the switch off the difference is exactly zero, so
        //nothing has to reproduce three's scaling to stay bit-identical.
        //
        //Unlike the WebGPU backend, this path has never had a leaf sun response at all - a
        //back-lit leaf got saturate(dot(n, L)) == 0. The switch is therefore what converges the
        //two backends, and off is what each of them rendered before.
        float leafRaw = dot( normal, directionalLights[ 0 ].direction );
        float leafBase = saturate( leafRaw );
        float leafHere = mix( leafBase, leafSun( leafRaw ), uLeafOn );
        float leafTerm = leafHere + uLeafT * max( leafSun( -leafRaw ) - leafHere, 0.0 );
        reflectedLight.directDiffuse += ( leafTerm - leafBase )
            * directionalLights[ 0 ].color * BRDF_Lambert( material.diffuseColor );
    #endif

    //Applied after the bleed, so light that reached this face through the leaf is shadowed and
    //occluded like any other. The WebGPU backend multiplies both faces by the same visibility
    //for the same reason.
    reflectedLight.directDiffuse *= densitySunTransmittance(vDensityWorld);

    //Flipped to the face being shaded. vDensityNormal is the un-flipped object normal, so a
    //leaf's UNDERSIDE was being occlusion-tested against the hemisphere above it - it inherited
    //its own topside's occlusion and went dark a second time, for a reason that had nothing to
    //do with where it actually is in the canopy. three flips only under DOUBLE_SIDED, and so
    //does this.
    vec3 leafDensityNormal = normalize(vDensityNormal);
    #ifdef DOUBLE_SIDED
        leafDensityNormal *= faceDirection;
    #endif
    irradiance *= densityOcclusion(vDensityWorld, leafDensityNormal);
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
