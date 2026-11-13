shader_type spatial;
// Cylinders, joints and root boxes: back-face culled Lambert with the same hemisphere and readout as the laminae.
render_mode cull_back, skip_vertex_transform, specular_disabled, depth_draw_opaque, diffuse_lambert;

#include "res://App/Shaders/agro_common.gdshaderinc"

uniform int mapping = 0;
uniform vec3 ramp_a = vec3(0.0);
uniform vec3 ramp_b = vec3(0.0);
uniform bool ramp_hsl = false;
uniform vec3 neutral_color = vec3(0.1);
uniform vec3 hemi_sky = vec3(3.175);
uniform vec3 hemi_ground = vec3(0.548);
uniform float analytic_hemi = 1.0;

varying vec3 v_world_normal;
varying flat vec4 v_custom;
varying flat vec3 v_instance_color;

void vertex() {
	VERTEX = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 n = normalize(agro_cofactor(mat3(MODEL_MATRIX)) * NORMAL);
	v_world_normal = n;
	NORMAL = normalize(mat3(VIEW_MATRIX) * n);
	v_custom = INSTANCE_CUSTOM;
	v_instance_color = COLOR.rgb;
}

void fragment() {
	vec3 n_world = normalize(v_world_normal);
	vec3 albedo = v_instance_color;
	vec3 emis = vec3(0.0);
	if (mapping != 0) {
		albedo = vec3(0.0);
		emis = agro_readout(mapping, v_custom, ramp_a, ramp_b, ramp_hsl, neutral_color);
	}
	ALBEDO = albedo;
	float w = 0.5 * n_world.y + 0.5;
	vec3 hemi = analytic_hemi > 0.5 ? mix(hemi_ground, hemi_sky, w) : vec3(0.0);
	EMISSION = emis + albedo * hemi;
	AO = 1.0;
}

// Godot multiplies DIFFUSE_LIGHT by the albedo after the light loop, so it must not be applied here.
void light() {
	DIFFUSE_LIGHT += LIGHT_COLOR / PI * ATTENUATION * max(dot(NORMAL, LIGHT), 0.0);
}
