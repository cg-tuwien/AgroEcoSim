import { h, Fragment } from "preact";
import { progress, settings, renderStill, renderAnimation, cancel, vramEstimateMB, frameCount } from "../../render/offline/OfflineRenderer";
import appstate from "../../appstate";

const presets: [string, number, number][] = [
    ["720p", 1280, 720],
    ["1080p", 1920, 1080],
    ["1440p", 2560, 1440],
    ["4K", 3840, 2160],
];

export function OfflineRender() {
    const p = progress.value;
    const busy = p.state === "running";
    const vram = vramEstimateMB();
    //Recomputed on render so it tracks history growing during a live simulation.
    //historyCount is the subscription handle: it changes whenever the history does,
    //including when a reload rebuilds it from the run index with no payloads resident.
    const frames = appstate.historyCount.value >= 0 ? frameCount() : 0;

    const tooltip = "Renders the current view at a chosen resolution, independent of the window size. "
        + "Useful for figures. Supersampling renders larger and averages down, which is slower but smoother.";

    return <>
        <h4 title={tooltip}>High-resolution render</h4>
        <div>
            <select name="offlinePreset" disabled={busy} onChange={e => {
                const found = presets.find(x => x[0] === e.currentTarget.value);
                if (found) { settings.width.value = found[1]; settings.height.value = found[2]; }
            }}>
                {/* Do NOT destructure this as [label, w, h]: `h` would shadow preact's JSX
                    factory inside the callback, and every <option> here would try to call a
                    number. It fails only once bundled, as a bare "n is not a function". */}
                {presets.map(preset =>
                    <option key={preset[0]} value={preset[0]}
                        selected={settings.width.value === preset[1] && settings.height.value === preset[2]}>
                        {preset[0]}
                    </option>)}
            </select>
            <label for="offlinePreset">Resolution</label>
        </div>
        <div>
            <input type="number" min={16} step={16} name="offlineWidth" disabled={busy}
                value={settings.width.value} onChange={e => settings.width.value = parseInt(e.currentTarget.value)} />
            <input type="number" min={16} step={16} name="offlineHeight" disabled={busy}
                value={settings.height.value} onChange={e => settings.height.value = parseInt(e.currentTarget.value)} />
            <label for="offlineWidth">Width x Height</label>
        </div>
        <div>
            <select name="offlineSsaa" disabled={busy} onChange={e => settings.ssaa.value = parseInt(e.currentTarget.value)}>
                {[1, 2, 3, 4].map(n => <option key={n} value={n} selected={settings.ssaa.value === n}>{n}x</option>)}
            </select>
            <label for="offlineSsaa">Supersampling</label>
        </div>
        <div>
            <select name="offlineCamera" disabled={busy} onChange={e => settings.camera.value = e.currentTarget.value as any}>
                <option value="current" selected={settings.camera.value === "current"}>current view</option>
                <option value="turntable" selected={settings.camera.value === "turntable"}>turntable</option>
            </select>
            <label for="offlineCamera">Camera</label>
        </div>
        <div>
            {/* Large targets are the most common way for this to fail outright, so the cost
                is shown before the button rather than after a context loss. */}
            <span style={{ color: vram > 512 ? "#c00" : "inherit" }}>~{vram} MB VRAM</span>
        </div>
        <div>
            <button name="renderStill" disabled={busy} onClick={() => renderStill()}>
                {busy ? "Rendering..." : "Render still (PNG)"}
            </button>
        </div>
        <h4>Animation</h4>
        <div>
            <input type="number" min={1} max={120} name="offlineFps" disabled={busy}
                value={settings.fps.value} onChange={e => settings.fps.value = parseInt(e.currentTarget.value)} />
            <label for="offlineFps">Frames per second</label>
        </div>
        <div>
            <select name="offlineTiming" disabled={busy} onChange={e => settings.timing.value = e.currentTarget.value as any}>
                <option value="uniform" selected={settings.timing.value === "uniform"}>every stored frame</option>
                <option value="simTime" selected={settings.timing.value === "simTime"}>by simulated time</option>
                <option value="noon" selected={settings.timing.value === "noon"}>one frame per day (noon)</option>
            </select>
            <label for="offlineTiming">Timing</label>
        </div>
        {settings.timing.value === "simTime" ? <div>
            <input type="number" min={1} step={1} name="offlineHoursPerSecond" disabled={busy}
                value={settings.hoursPerSecond.value} onChange={e => settings.hoursPerSecond.value = parseInt(e.currentTarget.value)} />
            <label for="offlineHoursPerSecond">Simulated hours per second</label>
        </div> : null}
        <div>
            <select name="offlineFormat" disabled={busy} onChange={e => settings.format.value = e.currentTarget.value as any}>
                <option value="webm" selected={settings.format.value === "webm"}>WebM video</option>
                <option value="jpeg-zip" selected={settings.format.value === "jpeg-zip"}>JPEG sequence (zip)</option>
                <option value="png-zip" selected={settings.format.value === "png-zip"}>PNG sequence (zip)</option>
            </select>
            <label for="offlineFormat">Format</label>
        </div>
        <div>
            <select name="offlineQuality" disabled={busy} onChange={e => settings.quality.value = e.currentTarget.value as any}>
                {["low", "medium", "high"].map(q =>
                    <option key={q} value={q} selected={settings.quality.value === q}>{q}</option>)}
            </select>
            <label for="offlineQuality">Video quality</label>
        </div>
        <div>
            {/* Reading history.length keeps this in step as previews stream in. */}
            <span>{frames} frames from {appstate.historyCount.value} stored
                {frames > 0 ? ` (${(frames / Math.max(1, settings.fps.value)).toFixed(1)} s)` : ""}</span>
        </div>
        <div>
            <button name="renderAnimation" disabled={busy || frames === 0} onClick={() => renderAnimation()}>
                {busy ? "Exporting..." : "Export animation"}
            </button>
            {busy ? <button name="cancelAnimation" onClick={() => cancel()}>Cancel</button> : null}
        </div>
        {p.state === "running" ? <div>
            <progress value={p.done} max={p.total} />
            <span> {p.done}/{p.total} · {p.phase}
                {p.msPerFrame > 0 && p.done < p.total
                    ? ` · ~${Math.round((p.total - p.done) * p.msPerFrame / 1000)} s left`
                    : ""}</span>
        </div> : null}
        {p.state === "error" ? <div style={{ color: "#c00" }}>{p.message}</div> : null}
        {p.state === "cancelled" ? <div>Export cancelled.</div> : null}
        {p.state === "done" ? <div>Saved {(p.bytes / 1048576).toFixed(2)} MB</div> : null}
    </>;
}
