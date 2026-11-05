import { h, Fragment } from "preact";
import appstate from "../../appstate";
import { IObstacleLight, IObstacleLightDocument, IObstacleLightMeta, IObstacleLightSurface, totalLeavesShed } from "../../helpers/ObstacleLight";

/**
 * Settings for the leaf area index measurement, and the export of its result.
 *
 * The filter is what turns the measurement on: it costs rays on every simulation step, so nothing
 * is measured until an obstacle name pattern is given. The per-tray vegetation survey needs no
 * filter - it runs for any imported tray system - and lands in the same document.
 */
export function LeafAreaIndex() {
    const obstaclesTip = "Case-insensitive regular expression of the obstacle names to measure: \"_T\\d{3}$\" for the \
trays of an imported facade, \"BACK_WALL\" for its walls, \"wall\" for hand-placed walls. Empty measures nothing. Requires \
the internal CPU light model - render mode 3, or 1 and 2 once they fall back to it.";
    const sensorsTip = "Measurement points spread over the selected obstacles in proportion to area. Cost follows this \
number rather than the triangle count, so a few thousand triangles per wall are free.";
    const depthTip = "How far in front of a surface foliage still counts towards its leaf area index, in metres. Leaves \
further away still shade it - they are counted in the irradiation - but they are not called part of its greening layer.";
    const bandTip = "Height of the horizontal bands each obstacle is resolved into, in metres. Foliage grows from the \
planting level upwards, so a wall reported as a single number hides that gradient. Set it above the tallest obstacle to \
get one band per object.";

    const doc = appstate.obstacleLight.value;
    //Same trap as ObstacleMapping: the server sends null, not undefined, when a run had nothing to
    //measure, and `null !== undefined` passes straight into `null.obstacles`.
    const measured = doc != null && doc.obstacles.length > 0;
    const surveyed = doc != null && doc.trays.length > 0;
    const shed = surveyed ? totalLeavesShed(doc!) : undefined;

    return <>
        <div>
            <input type="text" name="laiObstacles" placeholder="e.g. _T\d{3}$" title={obstaclesTip}
                value={appstate.leafAreaIndexObstacles.value}
                onChange={e => appstate.leafAreaIndexObstacles.value = e.currentTarget.value} />
            <label for="laiObstacles" title={obstaclesTip}>Measure obstacles matching</label>
        </div>
        <div>
            <input type="number" min={64} max={1000000} step={1024} name="laiSensors" title={sensorsTip}
                value={appstate.leafAreaIndexSensors.value}
                onChange={e => appstate.leafAreaIndexSensors.value = Math.max(64, parseInt(e.currentTarget.value, 10) || 64)} />
            <label for="laiSensors" title={sensorsTip}>Measurement points</label>
        </div>
        <div>
            <input type="number" min={0.05} max={50} step={0.5} name="laiDepth" title={depthTip}
                value={appstate.leafAreaIndexDepth.value}
                onChange={e => appstate.leafAreaIndexDepth.value = Math.max(0.05, parseFloat(e.currentTarget.value) || 0.05)} />
            <label for="laiDepth" title={depthTip}>Canopy depth (m)</label>
        </div>
        <div>
            <input type="number" min={0.05} max={100} step={0.25} name="laiBand" title={bandTip}
                value={appstate.leafAreaIndexBandHeight.value}
                onChange={e => appstate.leafAreaIndexBandHeight.value = Math.max(0.05, parseFloat(e.currentTarget.value) || 0.05)} />
            <label for="laiBand" title={bandTip}>Band height (m)</label>
        </div>
        <div>
            <button disabled={!measured && !surveyed} onClick={() => appstate.exportObstacleLight()}
                title="Writes the JSON document: weekly, annual and whole-run readings per obstacle, per facade side and per height band, the foliage of every tray, and a definitions block.">
                Export leaf area index (json)
            </button>
        </div>
        {surveyed ? <div><small title="Whole ordinary foliage blades removed during the run, including age replacement and stress shedding. Floral bracts and dead blades still attached are excluded.">
            {shed === undefined ? "Leaf shedding was not recorded in this run." : `Leaves shed during run: ${shed.toLocaleString()}.`}
        </small></div> : null}
        {measured
            ? <LeafAreaIndexTable doc={doc!} />
            : <Nothing meta={doc?.meta} surveyed={surveyed} />}
    </>;
}

/**
 * Why there is no measurement.
 *
 * The reasons are indistinguishable from the outside and one of them - a filter that matched
 * nothing - is entirely the user's to fix, so it has to say which.
 */
function Nothing({ meta, surveyed }: { meta: IObstacleLightMeta | undefined, surveyed: boolean }) {
    const trays = surveyed
        ? <div><small>The foliage of every tray was surveyed and is in the export.</small></div>
        : null;

    if (appstate.leafAreaIndexObstacles.value.trim().length === 0)
        return <>{trays}<div><small>No obstacle filter set, so no obstacle is measured.</small></div></>;

    if (meta && meta.obstaclesMatched === 0 && meta.obstaclesAvailable > 0)
        return <>{trays}<div><small>
            The filter <b>{meta.filter}</b> matched none of the {meta.obstaclesAvailable} obstacles in
            the scene. Names look like: {meta.obstacleNameSamples?.slice(0, 4).join(", ")}
        </small></div></>;

    if (meta && meta.obstaclesAvailable === 0)
        return <>{trays}<div><small>
            The scene has no obstacles to measure. Imported objects become obstacles unless their
            name ends in <b>_S&lt;n&gt;</b>, which marks soil.
        </small></div></>;

    return <>{trays}<div><small>No measurement yet. Run the simulation.</small></div></>;
}

/**
 * The measured obstacles, largest exposed area first, with the surface that dominates each.
 *
 * Shows the surface rather than only the object because an imported wall is a solid: its readings
 * average a planted face against a bare one, and the difference is a factor of two.
 */
function LeafAreaIndexTable({ doc }: { doc: IObstacleLightDocument }) {
    const light = doc.obstacles;
    const meta = doc.meta;
    const shown = [...light].sort((a, b) => b.exposedArea - a.exposedArea).slice(0, 24);
    //The selection's own totals when they are there: with many obstacles the per-obstacle series
    //are dropped for size, but the selection is always measured.
    const selection = doc.selection;
    const kWh = (o: { total: { avoided_kWh_m2: number }, exposedArea: number }) => o.total.avoided_kWh_m2 * o.exposedArea;
    const totalAvoided = selection ? kWh(selection) : light.reduce((a, o) => a + kWh(o), 0);
    const totalArea = selection?.exposedArea ?? light.reduce((a, o) => a + o.exposedArea, 0);

    const dominant = (o: IObstacleLight): IObstacleLightSurface | undefined => o.surfaces?.[0];

    return <>
        <h4>Measured: {light.length} obstacle{light.length === 1 ? "" : "s"}</h4>
        <div><small>
            {totalArea.toFixed(1)} m² exposed, {totalAvoided.toFixed(0)} kWh of solar gain kept off by foliage
            {meta?.renderer ? ` (${meta.renderer})` : ""}
            {doc.trays.length > 0 ? `; ${doc.trays.length} tray${doc.trays.length === 1 ? "" : "s"} surveyed` : ""}.
        </small></div>
        {meta?.filterError
            ? <div><small>
                The filter is not a valid regular expression ({meta.filterError}), so it was applied
                as a plain substring.
            </small></div>
            : null}
        {!doc.complete
            ? <div><small>
                Too many obstacles for a weekly series each over the socket, so what is shown here is
                their totals plus the series of the whole selection. The export fetches the complete
                document from the stored run.
            </small></div>
            : null}
        <table>
            <thead>
                <tr>
                    <th title="Name of the imported object">obstacle</th>
                    <th title="Exposed area, m²">m²</th>
                    <th title="Side and orientation of the surface carrying the most solar load">face</th>
                    <th title="Total one-sided leaf area per unit area of that surface, at its maximum during the run">peak LAI</th>
                    <th title="Share of the solar gain the foliage keeps off that surface">shaded</th>
                    <th title="Solar energy the foliage kept off the whole object">kWh</th>
                </tr>
            </thead>
            <tbody>
                {shown.map(o => {
                    const main = dominant(o);
                    return <tr key={o.name}>
                        <td>{o.name}</td>
                        <td>{o.exposedArea.toFixed(1)}</td>
                        <td>{main ? `${main.side} ${main.orientation} ${main.tiltDegrees.toFixed(0)}°` : "-"}</td>
                        <td>{((main ?? o).total.leafAreaIndexMax ?? (main ?? o).total.leafAreaIndex).toFixed(2)}</td>
                        <td>{(((main ?? o).total.shadedFraction) * 100).toFixed(1)} %</td>
                        <td>{kWh(o).toFixed(0)}</td>
                    </tr>;
                })}
            </tbody>
        </table>
        {light.length > shown.length
            ? <div><small>{light.length - shown.length} more in the exported file.</small></div>
            : null}
    </>;
}
