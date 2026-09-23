//Solar position, and the simulated clock it is evaluated at.
//
//This mirrors the server's notion of time so that a shadow in the viewport falls where the
//simulation believes the sun is. Every constant here has a counterpart in
//Agro/AgroWorld.cs. Cross-references name the C# MEMBER, not a line number: a line number is
//wrong the first time either file gains a comment, and a pointer that has been wrong once stops
//being checked at all.

//The site. These are DEFAULTS, not the values in use: latitude, longitude, altitude and the
//scene's azimuth are per-run settings the Terrain tab edits and the request carries. They
//remain named here because they are still what a run that says nothing gets, and because
//tests/sun.test.ts checks the solar geometry against published figures for this site.

/** Mirrors AgroWorld.DefaultLatitude. */
export const LATITUDE = 48.208333;
/** Mirrors AgroWorld.DefaultLongitude. */
export const LONGITUDE = 16.3725;
/** Metres above sea level. Mirrors AgroWorld.DefaultAltitude. */
export const ALTITUDE = 188;

/**
 * Which way the loaded terrain model faces, and the azimuth compensation each facing needs.
 *
 * The basic azimuth assumes a model facing BACKWARD - -Z, i.e. north - so that case alone needs
 * no compensation; most models face forward, half a turn away. The ORDER is the wire format: the
 * index is what the request and the site response carry, so it must stay locked to
 * Agro.TerrainOrientation in Agro/RequestModels/SimulationRequest.cs.
 */
export const AZIMUTH_OFFSETS = [
    { label: 'Forward', degrees: 180 },
    { label: 'Right', degrees: 90 },
    { label: 'Backward', degrees: 0 },
    { label: 'Left', degrees: -90 },
] as const;

/**
 * Rotation of the scene about the up axis, in degrees.
 *
 * Zero means the scene's +X - its right vector - points east, and an increasing value turns it
 * clockwise seen from above. It describes how the scene's axes are oriented on the compass and
 * rotates NOTHING in the scene graph; the only thing that moves is the sun. See directionFrom.
 */
export const AZIMUTH = 0;

/**
 * The site's time zone when the server has not said what it used.
 *
 * The server derives the zone from the coordinates (GeoTimeZone, in the AgroWorld ctor) and
 * there is no browser API that does the same, so a moved site MUST take the zone from run
 * metadata rather than guess: getting it wrong shifts every sun position, every shadow and the
 * whole sky by the offset, while still looking entirely plausible. This is only the fallback
 * for a run that carries no zone.
 */
export const DEFAULT_TIME_ZONE = 'Europe/Vienna';

/**
 * Everything about WHERE a run is that the sun and the sky depend on.
 *
 * One type rather than four loose numbers passed in four different orders: latitude and
 * longitude are trivially transposable, and the azimuth's sign is invisible until a shadow
 * points the wrong way.
 */
export type SolarSite = {
    /** Degrees north; negative is south. */
    latitude: number;
    /** Degrees east; negative is west. */
    longitude: number;
    /** Metres above sea level. */
    altitudeM: number;
    /** Scene rotation about the up axis; see AZIMUTH. */
    azimuthDeg: number;
    /** The zone the SERVER derived from the coordinates, or undefined for DEFAULT_TIME_ZONE. */
    timeZone?: string;
};

export const DEFAULT_SOLAR_SITE: SolarSite = {
    latitude: LATITUDE, longitude: LONGITUDE, altitudeM: ALTITUDE, azimuthDeg: AZIMUTH,
};

/**
 * Mirrors AgroWorld.InitialTime — the default when the request carries no InitialDate.
 *
 * Worth knowing: 1 February at 48°N is a very low sun, so a scene lit with the default date
 * looks far more raking than most people expect.
 */
export const DEFAULT_INITIAL_DATE = '2026-02-01';

/**
 * Reproduces the server's HoursPerTick snapping (Agro/AgroWorld.cs).
 *
 * The request's value is NOT what the simulation runs with: several inputs are silently
 * rounded to a divisor of the day. Using the requested value to advance the clock drifts the
 * sun by up to an hour per tick, which is exactly the kind of error that looks like a bug in
 * the solar maths instead of a units mismatch.
 */
export function snapHoursPerTick(requested: number): number {
    const h = Math.trunc(requested);
    if (h >= 24) return 24 * Math.trunc(h / 24);
    if (h <= 0) return 1;
    switch (h) {
        case 5: return 4;
        case 7: return 6;
        case 9: case 10: return 8;
        case 11: return 12;
        default:
            if (h > 12 && h <= 18) return 12;
            if (h > 18) return 24;
            return h;
    }
}

/**
 * The UTC instant of a timestep.
 *
 * Mirrors AgroWorld.GetTime: the initial date is a LOCAL wall-clock midnight at the site,
 * converted to UTC, then advanced by whole ticks.
 */
export function timeOfTimestep(initialDate: string, timestep: number, hoursPerTick: number,
                               timeZone = DEFAULT_TIME_ZONE): Date {
    const startUtc = localMidnightToUtc(initialDate, timeZone);
    return new Date(startUtc.getTime() + timestep * snapHoursPerTick(hoursPerTick) * 3600_000);
}

/**
 * Local midnight at the site, as a UTC instant.
 *
 * The site's zone is derived from its coordinates server-side (GeoTimeZone); here the IANA
 * name is resolved through Intl, which every supported browser has. Falling back to UTC on
 * failure shifts the sun by the zone offset rather than producing nonsense.
 */
export function localMidnightToUtc(initialDate: string, timeZone = DEFAULT_TIME_ZONE): Date {
    const [y, m, d] = initialDate.split('-').map(Number);
    const naive = Date.UTC(y, (m ?? 1) - 1, d ?? 1, 0, 0, 0);

    try {
        //Ask what that UTC instant reads as in the target zone, and correct by the difference.
        //Doing it this way avoids hard-coding DST rules.
        const offsetMs = zoneOffsetMs(new Date(naive), timeZone);
        return new Date(naive - offsetMs);
    } catch {
        return new Date(naive);
    }
}

function zoneOffsetMs(at: Date, timeZone: string): number {
    const parts = new Intl.DateTimeFormat('en-US', {
        timeZone, hour12: false,
        year: 'numeric', month: '2-digit', day: '2-digit',
        hour: '2-digit', minute: '2-digit', second: '2-digit',
    }).formatToParts(at);

    const get = (type: string) => Number(parts.find(p => p.type === type)?.value);
    //Intl renders midnight as hour 24 in some engines.
    const hour = get('hour') % 24;
    const asUtc = Date.UTC(get('year'), get('month') - 1, get('day'), hour, get('minute'), get('second'));
    return asUtc - at.getTime();
}

export type SolarPosition = {
    /** Degrees above the horizon. Negative when the sun is down. */
    altitude: number;
    /** Degrees clockwise from north. */
    azimuth: number;
    /** Unit vector in the scene's coordinates: +Y up, +X east, +Z south. */
    direction: { x: number; y: number; z: number };
};

const DEG = Math.PI / 180;

/**
 * NOAA solar position algorithm.
 *
 * Accurate to well under a degree for the years this simulation covers, which is far more
 * than a shadow direction needs, and it fits in a page with no dependency.
 */
export function solarPosition(when: Date, latitude = LATITUDE, longitude = LONGITUDE): SolarPosition {
    //Julian day, then the fraction of a Julian century since J2000.
    const julianDay = when.getTime() / 86400000 + 2440587.5;
    const t = (julianDay - 2451545) / 36525;

    const meanLongitude = mod360(280.46646 + t * (36000.76983 + t * 0.0003032));
    const meanAnomaly = 357.52911 + t * (35999.05029 - 0.0001537 * t);
    const eccentricity = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);

    const centre = Math.sin(meanAnomaly * DEG) * (1.914602 - t * (0.004817 + 0.000014 * t))
        + Math.sin(2 * meanAnomaly * DEG) * (0.019993 - 0.000101 * t)
        + Math.sin(3 * meanAnomaly * DEG) * 0.000289;

    const trueLongitude = meanLongitude + centre;
    const apparentLongitude = trueLongitude - 0.00569 - 0.00478 * Math.sin((125.04 - 1934.136 * t) * DEG);

    const obliquity = 23 + (26 + ((21.448 - t * (46.815 + t * (0.00059 - t * 0.001813)))) / 60) / 60;
    const correctedObliquity = obliquity + 0.00256 * Math.cos((125.04 - 1934.136 * t) * DEG);

    const declination = Math.asin(Math.sin(correctedObliquity * DEG) * Math.sin(apparentLongitude * DEG)) / DEG;

    const varY = Math.tan(correctedObliquity / 2 * DEG) ** 2;
    const equationOfTime = 4 * (varY * Math.sin(2 * meanLongitude * DEG)
        - 2 * eccentricity * Math.sin(meanAnomaly * DEG)
        + 4 * eccentricity * varY * Math.sin(meanAnomaly * DEG) * Math.cos(2 * meanLongitude * DEG)
        - 0.5 * varY * varY * Math.sin(4 * meanLongitude * DEG)
        - 1.25 * eccentricity * eccentricity * Math.sin(2 * meanAnomaly * DEG)) / DEG;

    //Minutes past UTC midnight, corrected to true solar time at this longitude.
    const utcMinutes = when.getUTCHours() * 60 + when.getUTCMinutes() + when.getUTCSeconds() / 60;
    const trueSolarTime = mod(utcMinutes + equationOfTime + 4 * longitude, 1440);
    const hourAngle = trueSolarTime / 4 < 0 ? trueSolarTime / 4 + 180 : trueSolarTime / 4 - 180;

    const latRad = latitude * DEG;
    const decRad = declination * DEG;
    const haRad = hourAngle * DEG;

    const cosZenith = Math.sin(latRad) * Math.sin(decRad)
        + Math.cos(latRad) * Math.cos(decRad) * Math.cos(haRad);
    const zenith = Math.acos(Math.max(-1, Math.min(1, cosZenith))) / DEG;
    const altitude = 90 - zenith;

    let azimuth: number;
    const denom = Math.cos(latRad) * Math.sin(zenith * DEG);
    if (Math.abs(denom) > 1e-9) {
        const cosAz = (Math.sin(latRad) * Math.cos(zenith * DEG) - Math.sin(decRad)) / denom;
        const base = Math.acos(Math.max(-1, Math.min(1, cosAz))) / DEG;
        azimuth = hourAngle > 0 ? mod360(base + 180) : mod360(540 - base);
    } else {
        //Sun directly overhead or at the pole; azimuth is undefined, pick south.
        azimuth = latitude > 0 ? 180 : 0;
    }

    return { altitude, azimuth, direction: directionFrom(altitude, azimuth) };
}

/**
 * Converts altitude/azimuth into the scene's axes.
 *
 * The field is laid out with +X east and +Z south (the terrain box grows from the origin
 * towards +X/+Z), so azimuth is measured clockwise from north = -Z.
 */
export function directionFrom(altitudeDeg: number, azimuthDeg: number) {
    const alt = altitudeDeg * DEG;
    const az = azimuthDeg * DEG;
    const horizontal = Math.cos(alt);
    return {
        x: horizontal * Math.sin(az),
        y: Math.sin(alt),
        z: -horizontal * Math.cos(az),
    };
}

function mod(value: number, m: number) {
    return ((value % m) + m) % m;
}

function mod360(value: number) {
    return mod(value, 360);
}
