import { h } from "preact";
import appstate from "../../appstate";

/**
 * Diagnostic view of the voxel GI's leaf-area-density field.
 *
 * Worth having as a control rather than a build flag: a wrong density field and a wrong cone
 * trace produce the same wrong image, and this is the only way to tell them apart.
 */
export function VoxelDebugControl() {
    const tooltip = "Draws the leaf-area-density field the voxel GI traces through: warm is dense, "
        + "cool is sparse. A diagnostic view of a rendering intermediate - it does not affect "
        + "the simulation, and it needs the WebGPU backend.";

    const show = appstate.showVoxels.value;

    return <div>
        <input type="checkbox" id="showVoxels" title={tooltip} checked={show}
            onChange={e => appstate.showVoxels.value = e.currentTarget.checked} />
        <label for="showVoxels" title={tooltip}>Show density field</label>
        {show
            ? <span>
                <label for="voxelMip" class="unit" title="Mip level. Higher levels are what a wide cone samples.">mip</label>
                <input type="number" id="voxelMip" min={0} max={7} step={1} value={appstate.voxelMip.value}
                    onChange={e => appstate.voxelMip.value = parseInt(e.currentTarget.value) || 0} />
                <label for="voxelGain" class="unit" title="Multiplies sigma for display only; the field itself is unchanged.">gain</label>
                <input type="number" id="voxelGain" min={0.1} max={64} step={0.5} value={appstate.voxelGain.value}
                    onChange={e => appstate.voxelGain.value = parseFloat(e.currentTarget.value) || 1} />
                <input type="checkbox" id="voxelAlbedo" checked={appstate.voxelAlbedo.value}
                    onChange={e => appstate.voxelAlbedo.value = e.currentTarget.checked} />
                <label for="voxelAlbedo" class="unit" title="Show the stored albedo instead of the density ramp.">albedo</label>
            </span>
            : null}
    </div>;
}
