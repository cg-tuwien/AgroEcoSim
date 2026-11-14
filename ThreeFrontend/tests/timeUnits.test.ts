import { effectiveStartOffsetHours, encodeTime, formatClock, formatElapsed, plantAgeInHours, plantAgeInUnit, readStartOffsetHours } from "../src/helpers/TimeUnits";
import { timeOfTimestep } from "../src/helpers/Sun";

describe("signed plant age", () => {
    it("uses exact nominal day and year units for either sign", () => {
        expect(plantAgeInHours(-2.5, "days")).toBe(-60);
        expect(plantAgeInHours(1.25, "years")).toBe(10950);
        expect(plantAgeInUnit(-8760, "years")).toBe(-1);
        expect(plantAgeInUnit(60, "days")).toBe(2.5);
    });
    it("quantizes toward the next tick on either side of recording start", () => {
        expect(effectiveStartOffsetHours(-25, 4)).toBe(-24);
        expect(effectiveStartOffsetHours(25, 4)).toBe(28);
        expect(effectiveStartOffsetHours(-0.5, 4)).toBe(0);
        expect(effectiveStartOffsetHours(0, 4)).toBe(0);
        expect(effectiveStartOffsetHours(-25, 5)).toBe(-24); // backend snaps 5 h to 4 h
        expect(effectiveStartOffsetHours(25, 25)).toBe(48); // backend snaps 25 h to 24 h
    });
    it("defaults old fields to zero and rejects nonfinite or nonnumeric imports", () => {
        expect(readStartOffsetHours(undefined)).toBe(0);
        expect(readStartOffsetHours(null)).toBe(0);
        for (const value of [NaN, Infinity, -Infinity, "24"])
            expect(() => readStartOffsetHours(value)).toThrow("finite");
    });
});

describe("encodeTime", () => {
    it("reads sensibly past one year", () => {
        //Used to read "2 year2 months" - the plural check and the unit were both wrong.
        expect(encodeTime(8760)).toBe("1 year");
        expect(encodeTime(2 * 8760)).toBe("2 years");
        expect(encodeTime(3 * 8760 + 100)).toBe("3 years");
    });
    it("keeps the smaller units", () => {
        expect(encodeTime(1)).toBe("1 hour");
        expect(encodeTime(6)).toBe("6 hours");
        expect(encodeTime(48)).toBe("2 days");
        expect(encodeTime(168)).toBe("1 week");
        expect(encodeTime(720)).toBe("1 month");
    });
});

describe("formatElapsed", () => {
    it("is exact at every scale", () => {
        expect(formatElapsed(0)).toBe("0 h");
        expect(formatElapsed(4)).toBe("4 h");
        expect(formatElapsed(24)).toBe("1 d");
        expect(formatElapsed(52)).toBe("2 d 4 h");
        expect(formatElapsed(8760)).toBe("1 y");
        expect(formatElapsed(8760 + 47 * 24 + 8)).toBe("1 y 47 d 8 h");
        expect(formatElapsed(8760 + 3)).toBe("1 y 3 h");
        expect(formatElapsed(2 * 8760 + 364 * 24 + 20)).toBe("2 y 364 d 20 h");
    });
    it("never goes negative", () => {
        expect(formatElapsed(-5)).toBe("0 h");
    });
});

describe("formatClock", () => {
    it("labels the instant in the site's zone, not the browser's", () => {
        const start = timeOfTimestep("2026-03-01", 0, 4, "Europe/Vienna");
        expect(formatClock(start, "Europe/Vienna")).toBe("2026-03-01 00:00");
        expect(formatClock(start, "UTC")).toBe("2026-02-28 23:00");
    });
    it("rolls over the year", () => {
        //359 days and 4 hours after 1 March 2026, on winter time again: 23 February 2027.
        const when = timeOfTimestep("2026-03-01", 1 + 359 * 6, 4, "Europe/Vienna");
        expect(formatClock(when, "Europe/Vienna")).toBe("2027-02-23 04:00");
    });
    it("follows daylight saving", () => {
        //Exactly 150 days of simulated hours after local midnight on 1 March lands on 29 July,
        //and the clocks changed on 29 March, so the wall clock reads one hour on.
        const summer = timeOfTimestep("2026-03-01", 150 * 6, 4, "Europe/Vienna");
        expect(formatClock(summer, "Europe/Vienna")).toBe("2026-07-29 01:00");
    });
    it("writes midnight as 00, not 24", () => {
        expect(formatClock(new Date(Date.UTC(2027, 0, 1, 0, 0)), "UTC")).toBe("2027-01-01 00:00");
    });
    it("falls back to UTC on an unknown zone", () => {
        expect(formatClock(new Date(Date.UTC(2027, 5, 1, 13, 5)), "Not/AZone")).toBe("2027-06-01 13:05");
    });
});
