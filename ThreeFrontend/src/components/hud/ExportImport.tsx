import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

export function ExportImport()
{
    const tooltipLoad = "Load simulation settings from a local file.";
    const tooltipSave = "Save simulation settings to a local file.";
    const tooltipGltf = "Export the last simulation frame to a gltf file.";

    return <div>
        <button onClick={async () => await appstate.load()} title={tooltipLoad}>Load</button>
        <button onClick={async () => await appstate.save()} title={tooltipSave}>Save</button>
        <button onClick={async () => await appstate.gltf()} title={tooltipGltf}>gltf</button>
    </div>;
}