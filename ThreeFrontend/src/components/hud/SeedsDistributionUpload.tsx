import { Fragment, h } from "preact";
import appstate from "../../appstate";
import { signal } from "@preact/signals";

const dragActive = signal(false);
export function SeedsDistributionUpload() {
    return <><div class={`dropdown-area${dragActive.value ? ' active' : ''}`} onDragOver={e => e.preventDefault()} onDragEnter={e => dragActive.value = true} onDragLeave={e => dragActive.value = false} onDrop={e => {
        e.preventDefault();
        const droppedFiles = e.dataTransfer.files;
        if (droppedFiles.length > 0) {
            Array.from(droppedFiles).forEach(async f => await appstate.uploadSeedsDistribution(f));
        }
    }}>
        DRAG & DROP SEEDS DISRTIBUTION (.json)
    </div>
    Selected: {appstate.seedsDistributionPath.value.length > 0 ? appstate.seedsDistributionPath.value : ""} {appstate.seedsDistributionPath.value?.length > 0
        ? <button onClick={e => appstate.clearSeedsDistribution()}>x</button>
        : <></>}
    </>
}