import { h, Fragment } from "preact";
import appstate from "../../appstate";
import { DEFAULT_TIME_ZONE, LATITUDE, LONGITUDE, solarPosition } from "../../helpers/Sun";

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
    const az = appstate.azimuth.value;

    const latTip = "Geographic latitude of the world origin, degrees north. Drives the sun, and "
        + "so the shadows, the light the plants receive and the sky. Negative is south.";
    const lonTip = "Geographic longitude of the world origin, degrees east. Sets solar time, and "
        + "the server derives the site's time zone from it. Negative is west.";
    const altTip = "Height of the world origin above sea level, in metres. A higher site has "
        + "less atmosphere above it, so the direct beam is stronger and the sky deeper.";
    const azTip = "Rotation of the scene about the up axis, in degrees. At 0 the scene's right "
        + "(+X) axis points east; increasing it turns the scene clockwise seen from above. It "
        + "rotates the SUN relative to the scene - no geometry moves.";

    //Midday at the site, as a quick sanity readout: a transposed latitude sign shows up here
    //before it shows up as a shadow pointing the wrong way.
    const noon = solarPosition(new Date(`${appstate.initialDate.value}T11:00:00Z`), lat, lon);
    //Azimuth 0 puts +X east, so that is where the scene's right axis points once turned.
    const bearing = ((az % 360) + 360) % 360;
    const compass = ['E', 'SE', 'S', 'SW', 'W', 'NW', 'N', 'NE'][Math.round(bearing / 45) & 7];

    const moved = lat !== LATITUDE || lon !== LONGITUDE;
    const zone = appstate.siteTimeZone.value;

    const num = (name: string, label: string, hint: string, signal: { value: number },
                 min: number, max: number, step: number, wrap = false) =>
        <div>
            <input type="number" min={min} max={max} step={step} name={name} title={hint}
                value={signal.value}
                onChange={e => {
                    const v = parseFloat(e.currentTarget.value);
                    if (!Number.isFinite(v)) { e.currentTarget.value = String(signal.value); return; }
                    //Clamped, not merely advisory: solarPosition returns NaN outside these
                    //ranges rather than throwing, and a NaN sun direction renders as a scene
                    //with no sun and nothing at all to point at the cause.
                    signal.value = wrap
                        ? ((v % max) + max) % max
                        : Math.min(max, Math.max(min, v));
                    e.currentTarget.value = String(signal.value);
                }} />
            <label for={name} title={hint}>{label}</label>
        </div>;

    return <>
        {num('siteLatitude', 'Latitude (°N)', latTip, appstate.latitude, -90, 90, 0.001)}
        {num('siteLongitude', 'Longitude (°E)', lonTip, appstate.longitude, -180, 180, 0.001)}
        {num('siteAltitude', 'Altitude (m)', altTip, appstate.altitude, -500, 9000, 10)}
        <div>
            {num('siteAzimuth', 'Scene azimuth (°)', azTip, appstate.azimuth, 0, 360, 5, true)}
            <label class="unit" title={azTip}>(+X points {compass})</label>
        </div>
        <div>
            <label class="unit">
                (midday sun {Number.isFinite(noon.altitude) ? noon.altitude.toFixed(0) : '?'}
                &deg; above the horizon
                {moved && !zone ? `, clock still on ${DEFAULT_TIME_ZONE}` : zone ? `, ${zone}` : ''})
            </label>
        </div>
    </>;
}
