import { h, Fragment } from "preact";
import appstate from "../../appstate";
import { AZIMUTH_OFFSETS, DEFAULT_TIME_ZONE, LATITUDE, LONGITUDE, solarPosition } from "../../helpers/Sun";
import type { Signal } from "@preact/signals";

/**
 * Where on Earth the world origin sits, and how the scene is turned on the compass.
 *
 * Free-typing inputs with a clamp, following LeafAreaIndex rather than FieldResolution or
 * FieldSizeX: those two emulate a spinner by ignoring the typed value and stepping from the
 * current one, which makes a coordinate like 48.2083 impossible to enter.
 *
 * The readouts matter as much as the inputs. Latitude and azimuth are both signs that are
 * invisible until a shadow points the wrong way, so the component says out loud which way the
 * scene faces and how high the midday sun gets - the two things that make a transposed sign
 * obvious at a glance.
 */
export function SiteLocation() {
    const lat = appstate.latitude.value;
    const lon = appstate.longitude.value;
    //The EFFECTIVE azimuth, base plus the terrain's own facing: this is what the sun is actually
    //placed against, so it is what the "+X points ..." readout below has to agree with.
    const az = appstate.azimuth.value;

    const latTip = "Geographic latitude of the world origin, degrees north. Drives the sun, and "
        + "so the shadows, the light the plants receive and the sky. Negative is south.";
    const lonTip = "Geographic longitude of the world origin, degrees east. Sets solar time, and "
        + "the server derives the site's time zone from it. Negative is west.";
    const altTip = "Height of the world origin above sea level, in metres. A higher site has "
        + "less atmosphere above it, so the direct beam is stronger and the sky deeper.";
    const azTip = "Rotation of the scene about the up axis, in degrees, before the terrain's own "
        + "facing is accounted for below. Increasing it turns the scene clockwise seen from "
        + "above. It rotates the SUN relative to the scene - no geometry moves.";
    const orientationTip = "Which way the loaded terrain model faces. The basic azimuth assumes "
        + "a model facing Backward (-Z, i.e. north), so that is the setting that needs no "
        + "compensation; most models face Forward instead, which is half a turn away.";

    //Midday at the site, as a quick sanity readout: a transposed latitude sign shows up here
    //before it shows up as a shadow pointing the wrong way.
    const noon = solarPosition(new Date(`${appstate.initialDate.value}T11:00:00Z`), lat, lon);
    //Azimuth 0 puts +X east, so that is where the scene's right axis points once turned.
    const bearing = ((az % 360) + 360) % 360;
    const compass = ['E', 'SE', 'S', 'SW', 'W', 'NW', 'N', 'NE'][Math.round(bearing / 45) & 7];

    const moved = lat !== LATITUDE || lon !== LONGITUDE;
    const zone = appstate.siteTimeZone.value;

    const num = (name: string, label: string, hint: string, data: Signal<number>, min: number, max: number, step: number, wrap = false) =>
        <div>
            <input type="number" min={min} max={max} step={step} name={name} title={hint}
                value={data.value}
                onChange={e => {
                    const v = parseFloat(e.currentTarget.value);
                    if (!Number.isFinite(v)) { e.currentTarget.value = String(data.value); return; }
                    //Clamped, not merely advisory: solarPosition returns NaN outside these
                    //ranges rather than throwing, and a NaN sun direction renders as a scene
                    //with no sun and nothing at all to point at the cause.
                    data.value = wrap
                        ? ((v % max) + max) % max
                        : Math.min(max, Math.max(min, v));
                    e.currentTarget.value = String(data.value);
                }} />
            <label for={name} title={hint}>{label}</label>
        </div>;

    return <>
        {num('siteLatitude', 'Latitude (°N)', latTip, appstate.latitude, -90, 90, 0.001)}
        {num('siteLongitude', 'Longitude (°E)', lonTip, appstate.longitude, -180, 180, 0.001)}
        {num('siteAltitude', 'Altitude (m)', altTip, appstate.altitude, -500, 9000, 10)}
        {num('siteAzimuth', 'Scene azimuth (°)', azTip, appstate.azimuthBase, 0, 360, 5, true)}
        <div>
            <select name="siteAzimuthOffset" title={orientationTip}
                value={appstate.azimuthOffset.value}
                onChange={e => appstate.azimuthOffset.value = Number(e.currentTarget.value)}>
                {AZIMUTH_OFFSETS.map((o, i) => <option key={o.label} value={i}>{o.label}</option>)}
            </select>
            <label for="siteAzimuthOffset" title={orientationTip}>Terrain faces</label>
        </div>
        <div>
            <h5>Info:</h5>
            <label class="unit" title={azTip}>(+X points {compass})</label>
            <label class="unit">
                (midday sun {Number.isFinite(noon.altitude) ? noon.altitude.toFixed(0) : '?'}
                &deg; above the horizon
                {moved && !zone ? `, clock still on ${DEFAULT_TIME_ZONE}` : zone ? `, ${zone}` : ''})
            </label>
        </div>
    </>;
}
