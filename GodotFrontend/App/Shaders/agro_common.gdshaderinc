// Shared pieces of the plant, terrain and obstacle shaders: the leaf BSDF of ThreeFrontend's
// mesh.wgsl.ts, the data-mapping readout decoded from the per-instance custom data, and the
// cofactor normal transform (the web's normalMatrix3) that stays correct under the per-instance
// non-uniform scale of a MultiMesh and finite for a degenerate zero-length organ.

// The leaf shading constants of core/Lighting.ts. Per renderer, not per species.
const float LEAF_WRAP = 0.7;
const float LEAF_ROUGHNESS = 0.6;
const float LEAF_TRANSMISSION = 0.4;

// Wrap-around diffuse plus Oren-Nayar flattening on the lit face, and transmission through to
// the shaded one. Call with -raw to get the other face.
float agro_leaf_sun(float raw) {
	float wrap = 1.0 + LEAF_WRAP;
	float front = clamp((raw + LEAF_WRAP) / wrap, 0.0, 1.0);
	float back = clamp((-raw + LEAF_WRAP) / wrap, 0.0, 1.0) * LEAF_TRANSMISSION;
	float s2 = LEAF_ROUGHNESS * LEAF_ROUGHNESS;
	float onA = 1.0 - 0.5 * s2 / (s2 + 0.33);
	float onB = 0.45 * s2 / (s2 + 0.09);
	float sinTheta = sqrt(clamp(1.0 - front * front, 0.0, 1.0));
	return front * (onA + onB * sinTheta) + back;
}

mat3 agro_cofactor(mat3 m) {
	vec3 a = m[0], b = m[1], c = m[2];
	return mat3(cross(b, c), cross(c, a), cross(a, b));
}

// three's Color.getHSL / setHSL over LINEAR values, hue lerped plainly.
vec3 agro_rgb2hsl(vec3 c) {
	float mx = max(c.r, max(c.g, c.b));
	float mn = min(c.r, min(c.g, c.b));
	float l = (mn + mx) * 0.5;
	float h = 0.0, s = 0.0;
	if (mn != mx) {
		float d = mx - mn;
		s = l <= 0.5 ? d / (mx + mn) : d / (2.0 - mx - mn);
		if (mx == c.r) h = (c.g - c.b) / d + (c.g < c.b ? 6.0 : 0.0);
		else if (mx == c.g) h = (c.b - c.r) / d + 2.0;
		else h = (c.r - c.g) / d + 4.0;
		h /= 6.0;
	}
	return vec3(h, s, l);
}

float agro_hue2rgb(float p, float q, float t) {
	if (t < 0.0) t += 1.0;
	if (t > 1.0) t -= 1.0;
	if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
	if (t < 0.5) return q;
	if (t < 2.0 / 3.0) return p + (q - p) * 6.0 * (2.0 / 3.0 - t);
	return p;
}

vec3 agro_hsl2rgb(vec3 hsl) {
	float h = hsl.x - floor(hsl.x);
	float s = clamp(hsl.y, 0.0, 1.0);
	float l = clamp(hsl.z, 0.0, 1.0);
	if (s == 0.0) return vec3(l);
	float p = l <= 0.5 ? l * (1.0 + s) : l + s - l * s;
	float q = 2.0 * l - p;
	return vec3(agro_hue2rgb(q, p, h + 1.0 / 3.0), agro_hue2rgb(q, p, h), agro_hue2rgb(q, p, h - 1.0 / 3.0));
}

vec3 agro_lerp_hsl(vec3 a, vec3 b, float t) {
	vec3 ha = agro_rgb2hsl(a), hb = agro_rgb2hsl(b);
	return agro_hsl2rgb(ha + (hb - ha) * t);
}

// Custom data layout (Core/Packing/InstanceEncoding.cs):
//   x = pack12(water, energy)   y = irradiance | woodRatio   z = pack12(relResource, relProduction)
//   w = round(clamp(auxins-1))*16 + organClass*2 + hasColor
// organClass: 0 lamina, 1 fruit/fruit wing, 2 cylinder, 3 root.
int agro_organ_class(vec4 c) { return int(mod(c.w, 16.0)) / 2; }

// Mapping codes: 0 natural, 1 water, 2 energy, 3 auxins, 4 cytokinins, 5 irradiance, 6 resource, 7 production.
// Returns the ramp input, or -1 when this organ shows the neutral colour under the mapping.
float agro_mapping_input(int mapping, vec4 c, int organ_class) {
	if (mapping == 1) return floor(c.x / 4096.0) / 4095.0;
	if (mapping == 2) return mod(c.x, 4096.0) / 4095.0;
	if (mapping == 3) return floor(c.w / 16.0) / 4095.0;
	if (mapping == 4) return 0.0;
	if (mapping == 5) return organ_class == 0 ? c.y * 1e-3 : -1.0;
	if (mapping == 6) return floor(c.z / 4096.0) / 4095.0;
	if (mapping == 7) return mod(c.z, 4096.0) / 4095.0;
	return -1.0;
}

// The unlit data readout: ramp endpoints are the active mapping's, in linear RGB, set by MaterialSet.
vec3 agro_readout(int mapping, vec4 custom, vec3 ramp_a, vec3 ramp_b, bool ramp_hsl, vec3 neutral) {
	float t = agro_mapping_input(mapping, custom, agro_organ_class(custom));
	if (t < 0.0) return neutral;
	return ramp_hsl ? agro_lerp_hsl(ramp_a, ramp_b, t) : mix(ramp_a, ramp_b, t);
}
