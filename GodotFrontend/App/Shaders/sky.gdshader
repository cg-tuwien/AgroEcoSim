shader_type sky;
// The simulation's own sky, per pixel: a port of render/core/SkyTexture.ts skyColor over the SkyDome model the CPU
// tracer integrates (CIE clear/overcast shapes, Rayleigh view and beam terms, per-channel Reinhard). The web fills
// a 128 px cube on the CPU; this evaluates the same maths per pixel, so it is model-identical, not texel-identical.
// The radiance (cubemap) pass instead returns the two-tone hemisphere light, which is what Godot's sky ambient,
// SSAO and SDFGI then use when one of those effects is switched on.

uniform vec3 sun_dir = vec3(0.0, 1.0, 0.0);
uniform float clear_scale = 0.0;
uniform float overcast_scale = 0.0;
uniform float sun_air_mass = 0.0;
uniform float ground_radiance = 0.0;
uniform float coverage = 0.0;
uniform vec3 ground_tint = vec3(1.2, 0.98, 0.72);
uniform vec3 hemi_sky = vec3(3.175);
uniform vec3 hemi_ground = vec3(0.514);

const vec3 RAYLEIGH_TAU = vec3(0.04181, 0.0977, 0.23853);
const float SKY_EXPOSURE = 0.3 / 60.7;
const float AEROSOL_NEUTRAL = 0.1;
const float MAX_SUN_AIR_MASS = 8.0;
const float REDDENING_AWAY = 0.25;

float fast_acos(float x) {
	bool negate = x < 0.0;
	float a = abs(x);
	float ret = -0.0187293 * a + 0.0742610;
	ret = ret * a - 0.2121144;
	ret = ret * a + 1.5707288;
	ret *= sqrt(max(0.0, 1.0 - a));
	return negate ? PI - ret : ret;
}

float air_mass(float elevation_deg) {
	float s = sin(radians(elevation_deg));
	return 1.0 / (s + 0.50572 * pow(elevation_deg + 6.07995, -1.6364));
}

void sky() {
	if (AT_CUBEMAP_PASS) {
		COLOR = EYEDIR.y > 0.0 ? hemi_sky : hemi_ground;
	} else {
		vec3 d = EYEDIR;
		if (d.y <= 0.0) {
			float v = ground_radiance * SKY_EXPOSURE;
			COLOR = (v / (1.0 + v)) * ground_tint;
		} else {
			float cg = clamp(dot(d, sun_dir), -1.0, 1.0);
			float gamma = fast_acos(cg);
			float gradation = 1.0 - exp(-0.32 / max(d.y, 1e-4));
			float overcast = 1.0 + 4.0 * exp(-0.7 / max(d.y, 1e-4));
			float indicatrix = 1.0 + 10.0 * (exp(-3.0 * gamma) - exp(-3.0 * PI * 0.5)) + 0.45 * cg * cg;
			float radiance = clear_scale * gradation * indicatrix + overcast_scale * overcast;
			float m_view = air_mass(degrees(asin(min(1.0, d.y))));
			vec3 scatter = 1.0 - exp(-RAYLEIGH_TAU * m_view);
			float towards = max(cg, 0.0);
			float m_beam = min(sun_air_mass, MAX_SUN_AIR_MASS) * (REDDENING_AWAY + (1.0 - REDDENING_AWAY) * towards);
			vec3 tint_in = scatter * exp(-RAYLEIGH_TAU * m_beam);
			float l = dot(tint_in, vec3(0.2126, 0.7152, 0.0722));
			float neutral = AEROSOL_NEUTRAL + coverage * (1.0 - AEROSOL_NEUTRAL);
			vec3 t = vec3(1.0);
			if (l > 1e-9) {
				vec3 n = tint_in / l;
				t = n + (1.0 - n) * neutral;
			}
			vec3 x = radiance * SKY_EXPOSURE * t;
			COLOR = x / (1.0 + x);
		}
	}
}
