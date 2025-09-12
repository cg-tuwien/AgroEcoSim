import { h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

export function DownloadRoots()
{
    const tooltip = "Downloads the roots geometry. Enable only if necessary, download and display of the roots system can significantly slow down the system.";
    return <div>
        <input type="checkbox" name="downloadRoots" title={tooltip} checked={appstate.downloadRoots} onChange={e => appstate.downloadRoots.value = e.currentTarget.checked }/>
        <label for="downloadRoots" title={tooltip}>Download roots</label>
    </div>;
}