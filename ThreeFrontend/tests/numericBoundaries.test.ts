import { exactSeed, parseSeedJson } from '../src/helpers/ExactSeed';
import { localMidnightToUtc, simulationClock } from '../src/helpers/Sun';
import { stepDate } from '../src/helpers/ObstacleLight';
import { mapSimTime, mapNoon, buildFrameMap } from '../src/render/offline/FrameSource';

const history = (n: number) => Array.from({ length: n }, (_, t) => ({ t, s: null }));

describe('exact seeds', () => {
    test.each(['0', '42', '9007199254740993', '18446744073709551615'])('round-trips %s', seed => {
        expect(exactSeed(parseSeedJson(`{"Seed":${seed}}`).Seed)).toBe(seed);
        expect(parseSeedJson(JSON.stringify({ Seed: seed })).Seed).toBe(seed);
    });
    test('does not rewrite seed-looking string contents', () => {
        const text = JSON.stringify({ comment: '"Seed":9007199254740993', Seed: '42' });
        expect(parseSeedJson(text)).toEqual(JSON.parse(text));
    });
    test.each(['-1', '1.5', '1e3', '18446744073709551616', 'NaN'])('rejects invalid seed %s', value => {
        expect(() => exactSeed(value)).toThrow();
    });
    test('rejects numbers whose bits are already lost', () => {
        expect(() => exactSeed(9007199254740992)).toThrow();
    });
});

describe('lazy animation plans', () => {
    test.each([0, -1, NaN, Infinity])('rejects speed %s without constructing frames', speed => {
        expect(() => mapSimTime(history(2), 0, 1, 25, speed, 4)).toThrow();
    });
    test.each([0, -1, NaN, Infinity])('rejects frame rate %s', fps => {
        expect(() => mapSimTime(history(2), 0, 1, fps, 7, 4)).toThrow();
    });
    test('includes the exact final state of a fractional duration', () => {
        const plan = mapSimTime(history(2), 0, 1, 25, 7, 4);
        expect(plan.at(0)).toEqual({ historyIndex: 0, simHours: 0 });
        expect(plan.at(plan.length - 1)).toEqual({ historyIndex: 1, simHours: 4 });
        expect(Array.from(plan).every(x => x.simHours <= 4)).toBe(true);
    });
    test('counts a large valid animation without allocating its frames', () => {
        const plan = mapSimTime(history(2), 0, 1, 25, 1e-9, 4);
        expect(plan.length).toBe(100000000001);
        expect(plan.at(plan.length - 1).historyIndex).toBe(1);
        expect(Object.keys(plan).length).toBeLessThan(5);
    });
    test('a single timestamp produces one frame', () => {
        expect(mapSimTime(history(1), 0, 0, 25, 7, 4).length).toBe(1);
    });
    test('rejects invalid strides and ranges', () => {
        expect(() => buildFrameMap(history(2), { mode: 'uniform', from: 0, to: 1, step: NaN, fps: 25, hoursPerSecond: 1, hoursPerTick: 4 })).toThrow();
    });
});

describe('the backend local calendar', () => {
    test.each([
        ['2026-09-27', 'Pacific/Auckland', '2026-09-26T12:00:00.000Z'],
        ['2026-10-04', 'Australia/Sydney', '2026-10-03T14:00:00.000Z'],
        ['2026-03-29', 'Europe/Vienna', '2026-03-28T23:00:00.000Z'],
    ])('resolves midnight on %s in %s', (date, zone, expected) => {
        expect(localMidnightToUtc(date, zone).toISOString()).toBe(expected);
    });
    test('assigns the LAI frame to the backend date after spring DST', () => {
        expect(stepDate('2026-03-15', 1, 503, 'Europe/Vienna')).toBe('2026-04-05');
    });
    test('selects actual local noon across spring and autumn DST', () => {
        for (const start of ['2026-03-15', '2026-10-15']) {
            const clock = simulationClock(start, 'Europe/Vienna');
            const frames = mapNoon(history(24 * 30), 0, 24 * 30 - 1, 1, start, 'Europe/Vienna');
            for (const frame of frames.slice(1, -1)) expect(clock(frame.simHours).getUTCHours()).toBe(12);
        }
    });
    test('matches backend local-start plus half-tick semantics at a transition', () => {
        // 24 elapsed hours after Mar 28 midnight is Mar 29 midnight; adding 24 local hours
        // differs from first advancing UTC by that amount across the spring clock change.
        expect(stepDate('2026-03-28', 48, 0, 'Europe/Vienna')).toBe('2026-03-29');
    });
});
