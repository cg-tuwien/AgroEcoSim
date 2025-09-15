import { Fragment, h } from "preact";
import appstate from "../../appstate";
import { signal } from "@preact/signals";

const dragActive = signal(false);
export function FieldModelUpload() {
    return <><div class={`dropdown-area${dragActive.value ? ' active' : ''}`} onDragOver={e => e.preventDefault()} onDragEnter={e => dragActive.value = true} onDragLeave={e => dragActive.value = false} onDrop={e => {
        e.preventDefault();
        const droppedFiles = e.dataTransfer.files;
        if (droppedFiles.length > 0) {
            Array.from(droppedFiles).forEach(async f => await appstate.uploadFieldModel(f));
        }
    }}>
        DRAG & DROP POTS MODEL (.obj)
    </div>
    Selected: {appstate.fieldModelPath.value.length > 0 ? appstate.fieldModelPath.value : ""} {appstate.fieldModelPath.value?.length > 0 ? <button onClick={e => appstate.clearFieldModel()}>x</button> : <></>} {appstate.modelParsingProgress.value > 0 ? appstate.modelParsingProgress.value : ""}
    </>
}