//Checkerboard stereo for the WebGL backend.
//
//Each eye renders the whole scene into its own render target through an off-axis frustum
//(core/Stereo.ts eyeFrustum, applied as PerspectiveCamera.filmOffset so three keeps the projection
//and its inverse consistent), then one full-screen pass interleaves the two on the canvas.
//
//The targets are HALF FLOAT where the context can render to it. A render target receives LINEAR
//colour in r152 (WebGLPrograms: outputColorSpace is Linear for any non-XR target), and so does the
//clear colour (getUnlitUniformColorSpace), so the composite applies the one LinearTosRGB the canvas
//path would have applied and an eye is that camera's mono view to within half-float rounding. The
//fallback is an sRGB8 target, which the hardware encodes on write and decodes on read.

import * as THREE from 'three';
import { convergenceDistance, eyeFrustum, MONO, stereoActive, type Eye, type StereoOptions } from '../core/Stereo';

const VERTEX = /* glsl */`
void main() {
    gl_Position = vec4(position.xy, 0.0, 1.0);
}`;

//Compiled as GLSL ES 3.00: three converts a non-raw ShaderMaterial on a WebGL2 context, which is
//what makes texelFetch available. gl_FragCoord is bottom-up in WebGL, so rows are counted from the
//top as height - 1 - y to match core/Stereo.ts eyeAt.
const FRAGMENT = /* glsl */`
uniform sampler2D leftEye;
uniform sampler2D rightEye;
uniform bool swapEyes;
uniform bool testPattern;
uniform float height;

void main() {
    ivec2 p = ivec2(gl_FragCoord.xy);
    int row = int(height) - 1 - p.y;
    bool left = ((p.x + row) & 1) == 0;
    if (swapEyes) left = !left;
    vec4 c = testPattern
        ? (left ? vec4(1.0, 0.0, 0.0, 1.0) : vec4(0.0, 0.0, 1.0, 1.0))
        : vec4((left ? texelFetch(leftEye, p, 0) : texelFetch(rightEye, p, 0)).rgb, 1.0);
    gl_FragColor = linearToOutputTexel(c);
}`;

export class ThreeStereo {
    options: StereoOptions = MONO;
    private targets: THREE.WebGLRenderTarget[] = [];
    private eye = new THREE.PerspectiveCamera();
    private quadScene = new THREE.Scene();
    private quadCamera = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
    private material = new THREE.ShaderMaterial({
        vertexShader: VERTEX,
        fragmentShader: FRAGMENT,
        uniforms: {
            leftEye: { value: null },
            rightEye: { value: null },
            swapEyes: { value: false },
            testPattern: { value: false },
            height: { value: 1 },
        },
        depthTest: false,
        depthWrite: false,
    });
    private warned = false;

    constructor() {
        const quad = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), this.material);
        quad.frustumCulled = false;
        this.quadScene.add(quad);
    }

    get active() { return stereoActive(this.options); }

    /** Renders both eyes and the interleave. False when this context cannot, so the caller draws mono. */
    render(renderer: THREE.WebGLRenderer, scene: THREE.Scene, camera: THREE.PerspectiveCamera, target: THREE.Vector3): boolean {
        if (!renderer.capabilities.isWebGL2) {
            if (!this.warned) console.warn('Stereo needs a WebGL2 context; drawing mono.');
            this.warned = true;
            return false;
        }
        const size = renderer.getDrawingBufferSize(new THREE.Vector2());
        this.ensureTargets(renderer, size.x, size.y);

        camera.updateMatrixWorld();
        const convergence = convergenceDistance(this.options, camera.position.distanceTo(target));
        const before = renderer.getRenderTarget();
        (['left', 'right'] as Eye[]).forEach((side, i) => {
            const f = eyeFrustum(side, this.options.eyeSeparation, convergence, camera.near, camera.fov, camera.aspect);
            const eye = this.eye;
            eye.copy(camera, false);
            eye.translateX(f.lateralOffset);
            //filmOffset shifts the window by near * filmOffset / filmWidth; solve for the shift we want.
            eye.filmOffset = f.frustumShift * eye.getFilmWidth() / camera.near;
            eye.updateProjectionMatrix();
            eye.updateMatrixWorld();
            renderer.setRenderTarget(this.targets[i]);
            renderer.render(scene, eye);
        });
        renderer.setRenderTarget(before);

        const u = this.material.uniforms;
        u.leftEye.value = this.targets[0].texture;
        u.rightEye.value = this.targets[1].texture;
        u.swapEyes.value = this.options.swapEyes;
        u.testPattern.value = this.options.testPattern;
        u.height.value = size.y;
        renderer.render(this.quadScene, this.quadCamera);
        return true;
    }

    /** Releases the eye targets; called when stereo is switched off and on dispose. */
    release() {
        for (const t of this.targets) t.dispose();
        this.targets = [];
    }

    dispose() {
        this.release();
        this.material.dispose();
        (this.quadScene.children[0] as THREE.Mesh).geometry.dispose();
    }

    private ensureTargets(renderer: THREE.WebGLRenderer, width: number, height: number) {
        if (this.targets.length === 2 && this.targets[0].width === width && this.targets[0].height === height) return;
        this.release();
        const float = renderer.extensions.has('EXT_color_buffer_float');
        const samples = Math.min(4, renderer.capabilities.maxSamples);
        for (let i = 0; i < 2; ++i) {
            const t = new THREE.WebGLRenderTarget(width, height, {
                type: float ? THREE.HalfFloatType : THREE.UnsignedByteType,
                samples,
                depthBuffer: true,
                minFilter: THREE.NearestFilter,
                magFilter: THREE.NearestFilter,
            });
            if (!float) t.texture.colorSpace = THREE.SRGBColorSpace;
            this.targets.push(t);
        }
        const bytesPerPixel = (float ? 8 : 4) * (samples + 1) + 4 * Math.max(1, samples);
        console.log(`Stereo eye targets: 2 x ${width}x${height}, ${float ? 'half float' : 'sRGB8'}, ${samples}x MSAA, `
            + `~${Math.round(2 * width * height * bytesPerPixel / 1048576)} MB`);
    }
}
