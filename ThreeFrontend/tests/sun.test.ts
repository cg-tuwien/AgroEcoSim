//Tests for the solar position and the simulated clock.
//
//The values are checked against published solar geometry for Vienna rather than against the
//implementation, so a transcription slip in the NOAA algorithm shows up as a wrong angle
//instead of a self-consistent one.

import {
    LATITUDE, LONGITUDE, DEFAULT_INITIAL_DATE,
    snapHoursPerTick, solarPosition, timeOfTimestep, localMidnightToUtc, directionFrom,
} from '../src/helpers/Sun';

describe('snapHoursPerTick mirrors AgroWorld', () => {
    test('values that divide the day pass through', () => {
        for (const h of [1, 2, 3, 4, 6, 8, 12]) expect(snapHoursPerTick(h)).toBe(h);
    });

    test('awkward values snap exactly as the server does', () => {
        //Agro/AgroWorld.cs: { <=0 => 1, 5 => 4, 7 => 6, 9 or 10 => 8, 11 => 12,
        //                     >12 and <=18 => 12, >18 => 24 }
        expect(snapHoursPerTick(0)).toBe(1);
        expect(snapHoursPerTick(-3)).toBe(1);
        expect(snapHoursPerTick(5)).toBe(4);
        expect(snapHoursPerTick(7)).toBe(6);
        expect(snapHoursPerTick(9)).toBe(8);
        expect(snapHoursPerTick(10)).toBe(8);
        expect(snapHoursPerTick(11)).toBe(12);
        for (const h of [13, 15, 18]) expect(snapHoursPerTick(h)).toBe(12);
        for (const h of [19, 22, 23]) expect(snapHoursPerTick(h)).toBe(24);
    });

    test('a day or more is rounded down to whole days', () => {
        expect(snapHoursPerTick(24)).toBe(24);
        expect(snapHoursPerTick(30)).toBe(24);
        expect(snapHoursPerTick(48)).toBe(48);
        expect(snapHoursPerTick(71)).toBe(48);
    });

    test('every result divides the day, or is a whole number of days', () => {
        for (let h = 1; h <= 200; ++h) {
            const s = snapHoursPerTick(h);
            expect(s >= 24 ? s % 24 === 0 : 24 % s === 0).toBe(true);
        }
    });
});

describe('solarPosition at Vienna', () => {
    //Solar noon in Vienna is a few minutes either side of 11:00 UTC because the city sits
    //east of its zone meridian.
    const noonish = (iso: string) => solarPosition(new Date(iso));

    test('summer solstice noon is high and due south', () => {
        const p = noonish('2026-06-21T11:00:00Z');
        //90 - latitude + obliquity = 90 - 48.21 + 23.44 ~= 65.2 degrees.
        expect(p.altitude).toBeGreaterThan(64);
        expect(p.altitude).toBeLessThan(66);
        expect(p.azimuth).toBeGreaterThan(170);
        expect(p.azimuth).toBeLessThan(190);
    });

    test('winter solstice noon is low and still due south', () => {
        const p = noonish('2026-12-21T11:00:00Z');
        //90 - latitude - obliquity = 90 - 48.21 - 23.44 ~= 18.3 degrees.
        expect(p.altitude).toBeGreaterThan(17);
        expect(p.altitude).toBeLessThan(20);
        expect(p.azimuth).toBeGreaterThan(170);
        expect(p.azimuth).toBeLessThan(190);
    });

    test('equinox noon sits between the two solstices', () => {
        const p = noonish('2026-03-20T11:00:00Z');
        //90 - latitude ~= 41.8 degrees.
        expect(p.altitude).toBeGreaterThan(40);
        expect(p.altitude).toBeLessThan(43.5);
    });

    test('the sun rises in the east and sets in the west', () => {
        const morning = solarPosition(new Date('2026-06-21T05:00:00Z'));
        const evening = solarPosition(new Date('2026-06-21T17:00:00Z'));
        expect(morning.azimuth).toBeGreaterThan(45);
        expect(morning.azimuth).toBeLessThan(135);
        expect(evening.azimuth).toBeGreaterThan(225);
        expect(evening.azimuth).toBeLessThan(315);
    });

    test('altitude is negative at local midnight', () => {
        expect(solarPosition(new Date('2026-06-21T23:00:00Z')).altitude).toBeLessThan(0);
        expect(solarPosition(new Date('2026-12-21T23:00:00Z')).altitude).toBeLessThan(0);
    });

    test('the default February start really is a low sun', () => {
        //Worth pinning: the backend uses this date whenever the request omits InitialDate,
        //and the resulting raking light surprises people.
        const p = noonish(`${DEFAULT_INITIAL_DATE}T11:00:00Z`);
        expect(p.altitude).toBeLessThan(28);
        expect(p.altitude).toBeGreaterThan(18);
    });

    test('coordinates default to the simulation site', () => {
        const a = solarPosition(new Date('2026-06-21T11:00:00Z'));
        const b = solarPosition(new Date('2026-06-21T11:00:00Z'), LATITUDE, LONGITUDE);
        expect(a.altitude).toBeCloseTo(b.altitude, 10);
    });
});

describe('directionFrom', () => {
    test('is a unit vector with +Y up', () => {
        for (const [alt, az] of [[65, 180], [10, 90], [0, 0], [45, 270]]) {
            const d = directionFrom(alt, az);
            expect(Math.hypot(d.x, d.y, d.z)).toBeCloseTo(1, 10);
            expect(d.y).toBeCloseTo(Math.sin(alt * Math.PI / 180), 10);
        }
    });

    test('azimuth is clockwise from north, with north at -Z', () => {
        const north = directionFrom(0, 0);
        expect(north.z).toBeCloseTo(-1, 10);
        const east = directionFrom(0, 90);
        expect(east.x).toBeCloseTo(1, 10);
        const south = directionFrom(0, 180);
        expect(south.z).toBeCloseTo(1, 10);
    });
});

describe('timeOfTimestep', () => {
    test('timestep 0 is local midnight on the initial date', () => {
        const t = timeOfTimestep('2026-06-21', 0, 4);
        //Vienna is UTC+2 in June, so local midnight is 22:00 UTC the previous day.
        expect(t.toISOString()).toBe('2026-06-20T22:00:00.000Z');
    });

    test('advances by the SNAPPED tick length, not the requested one', () => {
        //A requested 5 hours actually runs as 4. Using 5 here would drift the sun by an
        //hour every tick.
        const a = timeOfTimestep('2026-06-21', 1, 5);
        const b = timeOfTimestep('2026-06-21', 0, 5);
        expect((a.getTime() - b.getTime()) / 3600_000).toBe(4);
    });

    test('winter uses the standard-time offset', () => {
        //Vienna is UTC+1 in February.
        expect(timeOfTimestep('2026-02-01', 0, 1).toISOString()).toBe('2026-01-31T23:00:00.000Z');
    });

    test('six ticks of four hours is one day', () => {
        const start = timeOfTimestep('2026-06-21', 0, 4);
        const later = timeOfTimestep('2026-06-21', 6, 4);
        expect((later.getTime() - start.getTime()) / 3600_000).toBe(24);
    });
});

describe('localMidnightToUtc', () => {
    test('handles both sides of the daylight-saving switch', () => {
        expect(localMidnightToUtc('2026-01-15').toISOString()).toBe('2026-01-14T23:00:00.000Z');
        expect(localMidnightToUtc('2026-07-15').toISOString()).toBe('2026-07-14T22:00:00.000Z');
    });

    test('an unknown zone falls back to UTC instead of throwing', () => {
        const t = localMidnightToUtc('2026-07-15', 'Not/AZone');
        expect(t.toISOString()).toBe('2026-07-15T00:00:00.000Z');
    });
});
