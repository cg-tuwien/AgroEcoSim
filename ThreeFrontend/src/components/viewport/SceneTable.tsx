import { h } from "preact";
import appstate from "../../appstate";
import { useEffect } from "preact/hooks";
import { effect } from "@preact/signals";

export default function SceneTable() {
    let l = 0;
    //effect(() => { console.log("here " + appstate.scene.value.length); l = appstate.scene.value.length; });
    return <span><p>_ {l} _</p><div> {appstate.scene.value.length} </div></span>;
}