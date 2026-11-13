shader_type spatial;
// Laminae (leaves, petals; fruit wings and fruits with foliage = 0). Double-sided; Godot's side check flips the
// interpolated normal toward the viewer for cull_disabled materials, which is three's faceDirection flip.
render_mode cull_disabled, skip_vertex_transform, specular_disabled, depth_draw_opaque, diffuse_lambert;

#include "res://App/Shaders/agro_common.gdshaderinc"

uniform float foliage = 1.0;
uniform int mapping = 0;
uniform vec3 ramp_a = vec3(0.0);
uniform vec3 ramp_b = vec3(0.0);
uniform bool ramp_hsl = false;
uniform vec3 neutral_color = vec3(0.1);
uniform vec3 hemi_sky = vec3(3.175);
uniform vec3 hemi_ground = vec3(0.548);
uniform float analytic_hemi = 1.0;
uniform float leaf_translucency = 0.0;
uniform vec3 sun_dir_world = vec3(0.0, 1.0, 0.0);
uniform float shadow_caster_offset = 0.0;

varying vec3 v_world_normal;
varying flat vec4 v_custom;
varying flat vec3 v_instance_color;

void vertex() {
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// Shadow pass (orthographic): push every leaf's stored depth away from the sun, so a zero-thickness blade never
	// ties with itself in the shadow map while leaves further apart than the offset still shadow each other.
	if (PROJECTION_MATRIX[3][3] == 1.0) world -= sun_dir_world * shadow_caster_offset;
	VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
	vec3 n = normalize(agro_cofactor(mat3(MODEL_MATRIX)) * NORMAL);
	v_world_normal = n;
	NORMAL = normalize(mat3(VIEW_MATRIX) * n);
	v_custom = INSTANCE_CUSTOM;
	v_instance_color = COLOR.rgb;
}

void fragment() {
	vec3 n_world = normalize(v_world_normal);
	if (!FRONT_FACING) n_world = -n_world;
	vec3 albedo = v_instance_color;
	vec3 emis = vec3(0.0);
	if (mapping != 0) {
		albedo = vec3(0.0);
		emis = agro_readout(mapping, v_custom, ramp_a, ramp_b, ramp_hsl, neutral_color);
	}
	ALBEDO = albedo;
	// getHemisphereLightIrradiance on this face and on the far face; the brighter face bleeds through by t.
	float w = 0.5 * n_world.y + 0.5;
	vec3 here = mix(hemi_ground, hemi_sky, w);
	vec3 there = mix(hemi_ground, hemi_sky, 1.0 - w);
	float t = leaf_translucency * foliage;
	vec3 bleed = t * max(there - here, vec3(0.0));
	vec3 hemi = analytic_hemi > 0.5 ? here + bleed : bleed;
	EMISSION = emis + albedo * hemi;
	AO = 1.0;
}

// Godot multiplies DIFFUSE_LIGHT by the albedo after the light loop, so it must not be applied here.
void light() {
	if (LIGHT_IS_DIRECTIONAL) {
		float raw = dot(NORMAL, LIGHT);
		float here = foliage > 0.5 ? agro_leaf_sun(raw) : max(raw, 0.0);
		float there = foliage > 0.5 ? agro_leaf_sun(-raw) : max(-raw, 0.0);
		float ndl = here + leaf_translucency * foliage * max(there - here, 0.0);
		DIFFUSE_LIGHT += LIGHT_COLOR / PI * ATTENUATION * ndl;
	} else {
		DIFFUSE_LIGHT += LIGHT_COLOR / PI * ATTENUATION * max(dot(NORMAL, LIGHT), 0.0);
	}
}
