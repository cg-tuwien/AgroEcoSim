import { h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

export function Renderer()
{
    const tooltip = "Mitsuba3 provides a stable but slow global illumination. Tamashii slightly faster, but still experimental and unstable. Ambient light is a fallback for quick debugging, it assumes constant light everywhere.";
    return  <div>
        <select title={tooltip} name='renderMode' onChange={e => {appstate.renderMode.value = parseInt(e.currentTarget.value)}}>
            <option value={0} selected={appstate.renderMode.value == 0}>Ambient</option>
            <option value={1} selected={appstate.renderMode.value == 1}>Mitsuba3</option>
            <option value={2} selected={appstate.renderMode.value == 2}>Tamashii</option>
        </select>
        <label for='renderMode' title={tooltip}>Render mode</label>
    </div>;
}