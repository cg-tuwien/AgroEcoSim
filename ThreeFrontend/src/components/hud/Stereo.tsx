import { h } from "preact";
import appstate from "../../appstate";
import type { StereoMode } from "../../render/core/Stereo";

/**
 * Stereo presentation of the 3D view, for the institute's stereo LED wall.
 *
 * Checkerboard interleaves the two eyes pixel by pixel - even rows L R L R ..., odd rows
 * R L R L ... counted from the top-left corner of the view - which only survives when every
 * canvas pixel is one display pixel: browser zoom at 100 % and the view full screen (F11).
 */
export function Stereo() {
    const mode = appstate.stereoMode.value;
    const active = mode === 'checkerboard';
    const onTarget = appstate.stereoConvergeOnTarget.value;
    const tooltip = "How the 3D view is presented. Checkerboard renders the scene once per eye and gives "
        + "every pixel to one of them: even rows left, right, left, ..., odd rows right, left, right, ..., "
        + "counted from the top-left corner of the view. It needs one canvas pixel per display pixel, so "
        + "keep the browser zoom at 100 %. OpenXR is planned.";

    return <div>
        <div>
            <select title={tooltip} name="stereoMode" id="stereoMode"
                onChange={e => appstate.stereoMode.value = e.currentTarget.value as StereoMode}>
                <option value="mono" selected={mode === 'mono'}>Mono</option>
                <option value="checkerboard" selected={mode === 'checkerboard'}>Stereo Checkerboard</option>
                <option value="openxr" disabled>Stereo OpenXR (future)</option>
            </select>
            <label for="stereoMode" title={tooltip}>Stereo</label>
        </div>
        <div>
            <input type="number" id="stereoEyeSeparation" name="stereoEyeSeparation" min={0} max={10} step={0.005}
                disabled={!active} value={appstate.stereoEyeSeparation.value}
                title="Distance between the two virtual eyes, in metres. Larger separates near and far more strongly."
                onChange={e => {
                    const v = parseFloat(e.currentTarget.value);
                    if (Number.isFinite(v)) appstate.stereoEyeSeparation.value = Math.max(0, v);
                }} />
            <label for="stereoEyeSeparation">eye separation</label><label class="unit">m</label>
        </div>
        <div>
            <input type="checkbox" id="stereoConvergeOnTarget" name="stereoConvergeOnTarget" disabled={!active} checked={onTarget}
                title="Puts the zero-parallax plane through the orbit target: what the camera orbits sits on the display surface, nearer things come out of it."
                onChange={e => appstate.stereoConvergeOnTarget.value = e.currentTarget.checked} />
            <label for="stereoConvergeOnTarget">converge on the orbit target</label>
        </div>
        <div>
            <input type="number" id="stereoConvergence" name="stereoConvergence" min={0.05} max={1000} step={0.25}
                disabled={!active || onTarget} value={appstate.stereoConvergence.value}
                title="Distance of the zero-parallax plane from the camera, in metres, when it does not follow the orbit target."
                onChange={e => {
                    const v = parseFloat(e.currentTarget.value);
                    if (Number.isFinite(v)) appstate.stereoConvergence.value = Math.max(0.05, v);
                }} />
            <label for="stereoConvergence">convergence</label><label class="unit">m</label>
        </div>
        <div>
            <input type="checkbox" id="stereoSwapEyes" name="stereoSwapEyes" disabled={!active} checked={appstate.stereoSwapEyes.value}
                title="Exchange the eyes: for a view that starts on an odd display pixel, or a wall wired the other way."
                onChange={e => appstate.stereoSwapEyes.value = e.currentTarget.checked} />
            <label for="stereoSwapEyes">swap eyes</label>
            <input type="checkbox" id="stereoTestPattern" name="stereoTestPattern" disabled={!active} checked={appstate.stereoTestPattern.value}
                title="Left eye red, right eye blue: shows which eye sees which pixels through the glasses."
                onChange={e => appstate.stereoTestPattern.value = e.currentTarget.checked} />
            <label for="stereoTestPattern">test pattern</label>
        </div>
    </div>;
}
