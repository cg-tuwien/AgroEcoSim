import { h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

export function ExactPreview()
{
    const tooltip = "Forces each simulation step to be sent as a preview step. If deactivated, the backend will send previews as fast as the frontend can process them, often skipping a few step.";
    return <div>
        <input type="checkbox" name="exactPreview" title={tooltip} checked={appstate.exactPreview} onChange={e => appstate.exactPreview.value = e.currentTarget.checked }/>
        <label for="exactPreview" title={tooltip}>Exact preview</label>
    </div>;
}