//Stereo presentation of the viewport: the settings, the off-axis eye frusta and the checkerboard
//interleave both backends draw. Renderer-neutral, so the two backends cannot disagree about which
//pixel belongs to which eye.
//
//Written for the institute's stereo LED wall, after the RadFoam LED-wall viewer
//(github.com/gramegnadominik/vr-radfoam, LedWall.cs and led_wall.gdshader): every pixel of the
//display carries one eye, alternating along each row and starting on the other eye in the next.
//GodotFrontend/Core/Stereo/Stereo.cs is the same module for the Godot viewer; keep them diffable.

/** OpenXR is reserved: listed in the UI, not implemented yet. */
export type StereoMode = 'mono' | 'checkerboard' | 'openxr';

export type Eye = 'left' | 'right';

/**
 * Separation and convergence are in world units (metres).
 *
 * `convergeOnTarget` puts the zero-parallax plane through the orbit target, so whatever the camera
 * orbits sits on the display surface and nearer things come out of it; otherwise the plane is at
 * `convergence`. `swapEyes` corrects a display whose pixel grid starts on the other eye (a canvas
 * at an odd device-pixel offset, or a wall wired the other way); `testPattern` paints the left eye
 * red and the right eye blue to check exactly that.
 */
export type StereoOptions = {
    mode: StereoMode;
    eyeSeparation: number;
    convergeOnTarget: boolean;
    convergence: number;
    swapEyes: boolean;
    testPattern: boolean;
};

/** Six centimetres, as the RadFoam LED-wall viewer. */
export const DEFAULT_EYE_SEPARATION = 0.06;
export const DEFAULT_CONVERGENCE = 2;

export const MONO: StereoOptions = {
    mode: 'mono',
    eyeSeparation: DEFAULT_EYE_SEPARATION,
    convergeOnTarget: true,
    convergence: DEFAULT_CONVERGENCE,
    swapEyes: false,
    testPattern: false,
};

/** True when the view is rendered once per eye. */
export function stereoActive(o: StereoOptions | undefined): boolean {
    return o?.mode === 'checkerboard';
}

/**
 * The checkerboard interleave: pixel (x, y), counted from the TOP-LEFT corner from zero, belongs to
 * the left eye when x + y is even. Row 0 reads L R L R ..., row 1 reads R L R L ...
 *
 * Shaders reproduce this with gl_FragCoord / @builtin(position). WebGPU's origin is the top-left,
 * as here; WebGL's is the BOTTOM-left, so the WebGL composite counts rows as height - 1 - y.
 */
export function eyeAt(x: number, yFromTop: number, swap: boolean): Eye {
    return (((x + yFromTop) & 1) === 0) !== swap ? 'left' : 'right';
}

/** Distance of the zero-parallax plane from the camera. */
export function convergenceDistance(o: StereoOptions, targetDistance: number): number {
    return Math.max(o.convergeOnTarget ? targetDistance : o.convergence, 1e-4);
}

/**
 * One eye of an off-axis (parallel-axis, asymmetric-frustum) pair, after Paul Bourke's
 * "Calculating Stereo Pairs" and three's StereoCamera: the eye moves half the separation along the
 * camera's right axis, and its frustum window moves the other way by half the separation scaled to
 * the near plane, so both frusta meet in the convergence plane. No toe-in, so no vertical parallax.
 *
 * `lateralOffset` is the eye's translation along camera right; `frustumShift` the window's shift at
 * the near plane; `ndcShift` the same shift in NDC - what three's StereoCamera writes into element 8
 * of the projection, (right + left) / (right - left).
 */
export function eyeFrustum(eye: Eye, separation: number, convergence: number, near: number,
                           fovYDegrees: number, aspect: number) {
    const side = eye === 'left' ? -1 : 1;
    const half = separation * 0.5;
    const frustumShift = -side * half * near / convergence;
    const halfWidth = near * Math.tan((fovYDegrees * Math.PI) / 360) * aspect;
    return { lateralOffset: side * half, frustumShift, ndcShift: frustumShift / halfWidth };
}

/** Stored per browser, never in a session: it describes the display, not the scenario. */
const STORAGE_KEY = 'agro.stereo';

export function loadStereoOptions(): StereoOptions {
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (!raw) return { ...MONO };
        const o = JSON.parse(raw) as Partial<StereoOptions>;
        return {
            //OpenXR is listed but not built; a stored choice of it opens in mono.
            mode: o.mode === 'checkerboard' ? 'checkerboard' : 'mono',
            eyeSeparation: Number.isFinite(o.eyeSeparation) ? Math.max(0, o.eyeSeparation!) : DEFAULT_EYE_SEPARATION,
            convergeOnTarget: o.convergeOnTarget ?? true,
            convergence: Number.isFinite(o.convergence) ? Math.max(0.05, o.convergence!) : DEFAULT_CONVERGENCE,
            swapEyes: !!o.swapEyes,
            //A calibration aid: never comes back by itself.
            testPattern: false,
        };
    } catch {
        return { ...MONO };
    }
}

export function saveStereoOptions(o: StereoOptions) {
    try {
        const { testPattern: _, ...stored } = o;
        localStorage.setItem(STORAGE_KEY, JSON.stringify(stored));
    } catch { /* private mode or blocked storage: the setting just does not persist */ }
}
