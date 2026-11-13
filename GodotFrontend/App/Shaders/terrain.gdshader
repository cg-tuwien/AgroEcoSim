shader_type spatial;
// Soil boxes, trays, walls, umbrellas and imported obstacles: flat-shaded, double-sided Lambert. The flat normal
// comes from screen derivatives and is turned toward the viewer by a dot test, which sidesteps the derivative-sign
// trap the web's WebGPU backend hit. `painted` obstacles are unlit vertex colours (the obstacle data mapping).
render_mode cull_disabled, specular_disabled, depth_draw_opaque, diffuse_lambert;

uniform vec3 albedo_color : source_color = vec3(0.34, 0.21, 0.0);
uniform float painted = 0.0;
uniform float depth_bias = 0.0;
uniform float measured_bias = 0.0;
uniform vec3 hemi_sky = vec3(3.175);
uniform vec3 hemi_ground = vec3(0.548);
uniform float analytic_hemi = 1.0;

varying vec3 v_view_pos;
varying vec4 v_vertex_color;

void vertex() {
	v_vertex_color = COLOR;
	// a relative nudge toward the camera stands in for polygon offset (view z is negative in front)
	float nudge = depth_bias + measured_bias * COLOR.a;
	v_view_pos = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 n = normalize(cross(dFdx(v_view_pos), dFdy(v_view_pos)));
	if (dot(n, -v_view_pos) < 0.0) n = -n;
	NORMAL = n;
	vec3 n_world = normalize((INV_VIEW_MATRIX * vec4(n, 0.0)).xyz);
	if (painted > 0.5) {
		ALBEDO = vec3(0.0);
		EMISSION = v_vertex_color.rgb;
	} else {
		ALBEDO = albedo_color;
		float w = 0.5 * n_world.y + 0.5;
		vec3 hemi = analytic_hemi > 0.5 ? mix(hemi_ground, hemi_sky, w) : vec3(0.0);
		EMISSION = albedo_color * hemi;
	}
	AO = 1.0;
}

// Godot multiplies DIFFUSE_LIGHT by the albedo after the light loop, so it must not be applied here.
void light() {
	DIFFUSE_LIGHT += LIGHT_COLOR / PI * ATTENUATION * max(dot(NORMAL, LIGHT), 0.0);
}
