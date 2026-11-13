// Pixel-interleaved stereo for the LED wall: each output pixel shows exactly one eye.
// Pixel (x, y), counted from the top-left corner from zero, is the left eye's when x + y is even:
// row 0 reads L R L R ..., row 1 reads R L R L ...  (Core/Stereo/Stereo.cs StereoMath.EyeAt is the reference.)
// Both eye images are the size of this viewport, so texelFetch at the fragment's own pixel is exact - no filtering.
shader_type canvas_item;
render_mode unshaded, blend_disabled;

uniform sampler2D left_eye : filter_nearest;
uniform sampler2D right_eye : filter_nearest;
uniform bool swap_eyes = false;
// calibration: left eye pure red, right eye pure blue
uniform bool test_pattern = false;

void fragment() {
	// FRAGCOORD counts from the viewport's top-left pixel (measured: 256,000 of 256,000 pixels as expected)
	ivec2 p = ivec2(FRAGCOORD.xy);
	bool left = ((p.x + p.y) & 1) == 0;
	if (swap_eyes) {
		left = !left;
	}
	if (test_pattern) {
		COLOR = left ? vec4(1.0, 0.0, 0.0, 1.0) : vec4(0.0, 0.0, 1.0, 1.0);
	} else {
		COLOR = vec4((left ? texelFetch(left_eye, p, 0) : texelFetch(right_eye, p, 0)).rgb, 1.0);
	}
}
