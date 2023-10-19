import { h, Fragment } from "preact";
import appstate from "../../appstate";
import { VisualMappingOptions } from "../../helpers/Plant";
//import "wired-elements"

export function VisualMapping() {
    const tooltipVisualMapping = "natural (green/brown by wood factor) \
    water (relative amount of water wrt. storage available in the plant part) \
    energy (relative amount of enery wrt. storage available in the plant part) \
    resource (relative success in resource acquisition wrt. to other plant parts) \
    production (relative success in producing new resources wrt. to other plant parts)";
    const tooltipDebugOrientations = "Shows XYZ/RGB boxes instead of standard geometry.";
    return <>
        <div>
            <select title={tooltipVisualMapping} name='visualmapping' onChange={e => appstate.visualMapping.value = e.currentTarget.value as VisualMappingOptions}>
                {
                    Object.values(VisualMappingOptions).map(x => (<option value={x} selected={appstate.visualMapping.value == x}>{x}</option>))
                }
            </select>
            <label for='visualmapping' title={tooltipVisualMapping}>visual mapping</label>
        </div>
        <div>
            <input type="checkbox" name="debugOrientations" title={tooltipDebugOrientations} checked={appstate.debugBoxes} onChange={e => appstate.debugBoxes.value = e.currentTarget.checked }/>
            <label for='debugOrientations' title={tooltipDebugOrientations}>debug orientations</label>
        </div>
        <div>
            <input type="checkbox" name="showLeaves" checked={appstate.showLeaves} onChange={e => appstate.showLeaves.value = e.currentTarget.checked }/>
            <label for='showLeaves'>show leaves</label>
        </div>
        <div>
            <input type="checkbox" name="showTerrain" checked={appstate.showTerrain} onChange={e => appstate.showTerrain.value = e.currentTarget.checked }/>
            <label for='showTerain'>show terrain</label>
        </div>
        <div>
            <input type="checkbox" name="showRoots" checked={appstate.showRoots} onChange={e => appstate.showRoots.value = e.currentTarget.checked }/>
            <label for='showRoots'>show roots</label>
        </div>
    </>;
}