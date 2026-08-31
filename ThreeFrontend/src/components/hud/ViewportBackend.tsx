import { h } from "preact";
import appstate from "../../appstate";

export function ViewportBackend() {
    //Worded to keep this apart from "Render mode", which selects the SERVER's light-transport
    //engine and changes what the simulation computes. Choosing a graphics API here changes
    //nothing about the simulation.
    const tooltip = "Which graphics API draws the 3D view. Unrelated to Render mode "
        + "(Ambient / Mitsuba3 / Tamashii), which selects the light-transport engine the "
        + "server uses to compute plant irradiance. On auto, WebGPU is tried first and WebGL "
        + "is used if it fails. Choosing a backend explicitly keeps it on every tab; on auto, "
        + "editing tabs switch to WebGL for its axis gizmo.";

    const active = appstate.activeBackend.value;

    return <div>
        <select title={tooltip} name="viewportBackend"
            onChange={e => appstate.viewportBackend.value = e.currentTarget.value as any}>
            <option value="auto" selected={appstate.viewportBackend.value === "auto"}>auto (prefer WebGPU)</option>
            <option value="three" selected={appstate.viewportBackend.value === "three"}>WebGL</option>
            <option value="webgpu" selected={appstate.viewportBackend.value === "webgpu"}>WebGPU (experimental)</option>
        </select>
        <label for="viewportBackend" title={tooltip}>Viewport GPU API</label>
        {active ? <label class="unit">(active: {active === "three" ? "WebGL" : "WebGPU"})</label> : null}
    </div>;
}
