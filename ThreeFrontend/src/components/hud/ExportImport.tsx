import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

export function ExportImport()
{
    return <div>
        <button onClick={async () => await appstate.load()}>Load</button>
        <button onClick={async () => await appstate.save()}>Save</button>
        <button onClick={async () => await appstate.gltf()}>gltf</button>
    </div>;
}