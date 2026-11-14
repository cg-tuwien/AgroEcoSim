import { h } from "preact";
import { useEffect, useState } from "preact/hooks";
import appstate, { sceneAtSync } from "../../appstate";
import { downloadText } from "../../helpers/download";
import { exportCurrentFrame, exportRootsAvailable } from "../../render/export/CurrentFrameExport";

export function ExportImport() {
    const [roots, setRoots] = useState(false);
    const [soilAndTrays, setSoilAndTrays] = useState(true);
    const [obstacles, setObstacles] = useState(true);
    const [metadata, setMetadata] = useState(false);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState("");
    const [warnings, setWarnings] = useState<string[]>([]);
    const shownRootsAvailable = exportRootsAvailable(appstate.scene.value);
    const selectedRootsAvailable = exportRootsAvailable(sceneAtSync(appstate.playPointer.value) ?? []);
    const rootsAvailable = shownRootsAvailable && selectedRootsAvailable;

    useEffect(() => {
        if (!rootsAvailable) setRoots(false);
    }, [rootsAvailable]);

    const exportFrame = async () => {
        setBusy(true);
        setError("");
        setWarnings([]);
        try {
            const result = await exportCurrentFrame({ roots: roots && rootsAvailable, soilAndTrays, obstacles, metadata });
            downloadText(result.json, "gltf", `_step-${result.step}`);
            setWarnings(result.warnings);
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : String(reason));
        } finally {
            setBusy(false);
        }
    };

    return <div>
        <button disabled={busy} onClick={() => appstate.load()}
            title="Load a run from an .agro archive or a legacy JSON file.">Load run</button>
        <h4>Current-frame geometry</h4>
        <div>
            <input id="exportRoots" type="checkbox" checked={roots && rootsAvailable}
                disabled={busy || !rootsAvailable} onChange={e => setRoots(e.currentTarget.checked)} />
            <label for="exportRoots" title={rootsAvailable
                ? "Include the downloaded root geometry, even when hidden in the viewport."
                : "The selected frame has no downloaded root geometry."}>Roots</label>
        </div>
        <div>
            <input id="exportSoilAndTrays" type="checkbox" checked={soilAndTrays}
                disabled={busy} onChange={e => setSoilAndTrays(e.currentTarget.checked)} />
            <label for="exportSoilAndTrays">Soil and trays</label>
        </div>
        <div>
            <input id="exportObstacles" type="checkbox" checked={obstacles}
                disabled={busy} onChange={e => setObstacles(e.currentTarget.checked)} />
            <label for="exportObstacles">Other obstacles</label>
        </div>
        <div>
            <input id="exportMetadata" type="checkbox" checked={metadata}
                disabled={busy} onChange={e => setMetadata(e.currentTarget.checked)} />
            <label for="exportMetadata" title="Include recorded organ statistics and their definitions in GLTF extras.">Include metadata</label>
        </div>
        <button name="exportCurrentFrame" disabled={busy || appstate.historyCount.value === 0}
            onClick={exportFrame} title="Export the selected playhead frame in natural colours. The checkboxes control inclusion independently of viewport visibility.">
            {busy ? "Exporting frame..." : "Export current frame (.gltf)"}
        </button>
        {error ? <div role="alert">{error}</div> : null}
        {warnings.map(warning => <div key={warning} role="status">{warning}</div>)}
    </div>;
}
