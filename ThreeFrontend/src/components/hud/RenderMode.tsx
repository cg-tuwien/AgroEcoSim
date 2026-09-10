import { h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

export function Renderer()
{
    const tooltip = "Internal is the built-in CPU light model: sun and sky with shadows and one bounce, \
and the only mode that measures leaf area index on obstacles. Mitsuba3 provides a stable but slow global \
illumination, Tamashii is faster but experimental; both fall back to Internal when their renderer does not \
answer. Ambient assumes constant light everywhere and is a fallback for quick debugging.";
    return  <div>
        <select title={tooltip} name='renderMode' onChange={e => {appstate.renderMode.value = parseInt(e.currentTarget.value)}}>
            <option value={0} selected={appstate.renderMode.value == 0}>Ambient</option>
            <option value={1} selected={appstate.renderMode.value == 1}>Mitsuba3</option>
            <option value={2} selected={appstate.renderMode.value == 2}>Tamashii</option>
            <option value={3} selected={appstate.renderMode.value == 3}>Internal (CPU)</option>
        </select>
        <label for='renderMode' title={tooltip}>Render mode</label>
    </div>;
}