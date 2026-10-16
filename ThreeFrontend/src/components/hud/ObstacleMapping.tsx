import { h, Fragment } from "preact";
import appstate from "../../appstate";
import { ObstacleMappingOptions, obstacleMappingSpec } from "../../helpers/ObstacleLight";
import { obstacleColor } from "../../render/core/ObstacleColors";
import { linearToSrgbRGB } from "../../render/core/Color";

/**
 * Colours the measured obstacles in the viewport by one of their readings, with a legend.
 *
 * The series is weekly, so the colours follow the playback date and change as the canopy develops
 * over the season rather than over the hours of a day - which is what these quantities are: a
 * canopy grows over weeks, and a weekly solar total is what the study asked for.
 */
export function ObstacleMapping() {
    const tooltip = "Colours the obstacles that were measured. Leaf area index is the total one-sided leaf area per \
unit facade area. Shaded fraction is the share of solar gain the foliage keeps off - the number a cooling estimate wants. \
Avoided irradiation is that share as energy. Beam transmittance is how much direct sun still gets through.";

    const mapping = appstate.obstacleMapping.value;
    const doc = appstate.obstacleLight.value;
    //`!= null`, not `!== undefined`. The server sends `obstacleLight: null` whenever a run had
    //nothing to measure, and `null !== undefined` is true, so a stricter test read `.obstacles`
    //off null and threw inside this component's render - after almost every simulation, since a
    //run with no obstacles is the ordinary case.
    const measured = doc != null && doc.obstacles.length > 0;
    const date = appstate.shownObstacleDate.value;

    return <>
        <div>
            <select title={tooltip} name="obstacleMapping" disabled={!measured}
                onChange={e => appstate.obstacleMapping.value = e.currentTarget.value as ObstacleMappingOptions}>
                {Object.values(ObstacleMappingOptions).map(x =>
                    <option key={x} value={x} selected={mapping === x}>{x}</option>)}
            </select>
            <label for="obstacleMapping" title={tooltip}>obstacle mapping</label>
        </div>
        {measured && mapping !== ObstacleMappingOptions.None
            ? <Legend mapping={mapping} date={date} />
            : null}
        {!measured
            ? <div><small>Nothing measured. Set an obstacle filter in Analysis and run with the internal light model.</small></div>
            : null}
    </>;
}

const STOPS = 12;

function Legend({ mapping, date }: { mapping: Exclude<ObstacleMappingOptions, ObstacleMappingOptions.None>, date: string | undefined }) {
    const spec = obstacleMappingSpec[mapping];
    if (!spec) return null;
    const [min, max] = appstate.obstacleMappingRange.value;

    //The ramp is sampled rather than described as a CSS gradient, so the swatch is exactly the
    //colours the renderer produces - including the HSL interpolation between its five stops.
    const swatch = [];
    for (let i = 0; i < STOPS; ++i) {
        const c = obstacleColor(min + (i + 0.5) / STOPS * (max - min), min, max);
        const srgb = linearToSrgbRGB(c);
        swatch.push(<span key={i} style={{
            background: `rgb(${Math.round(srgb.r * 255)},${Math.round(srgb.g * 255)},${Math.round(srgb.b * 255)})`,
            display: 'inline-block', width: `${100 / STOPS}%`, height: '0.8em',
        }} />);
    }

    return <div>
        <div style={{ lineHeight: 0 }}>{swatch}</div>
        <small>
            {spec.format(min)} &ndash; {spec.format(max)}{spec.unit ? ` ${spec.unit}` : ""}
            {date ? ` · ${date}` : ""}
        </small>
    </div>;
}
