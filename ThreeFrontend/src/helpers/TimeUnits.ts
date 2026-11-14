import { snapHoursPerTick } from "./Sun";

export type PlantAgeUnit = "days" | "years";

/** The Age control uses signed elapsed time, never calendar-year arithmetic. */
export const plantAgeUnitHours = (unit: PlantAgeUnit): number => unit === "years" ? 8760 : 24;
export const plantAgeInUnit = (hours: number, unit: PlantAgeUnit): number => hours / plantAgeUnitHours(unit);
export const plantAgeInHours = (value: number, unit: PlantAgeUnit): number => value * plantAgeUnitHours(unit);

/** Missing fields in older files mean a seed at recording start. Invalid values must not become zero. */
export function readStartOffsetHours(value: unknown): number {
    if (value === undefined || value === null) return 0;
    if (typeof value !== "number" || !Number.isFinite(value))
        throw new Error("Plant Age must be a finite number of days or years.");
    return value;
}

/** First simulation tick at or after seeding, matching the server for either sign. */
export function effectiveStartOffsetHours(hours: number, requestedHoursPerTick: number): number {
    const tick = snapHoursPerTick(requestedHoursPerTick);
    return Math.ceil(readStartOffsetHours(hours) / tick) * tick || 0;
}

export const encodeTime = (t: number) => {
    if (t == 1) return  "1 hour";
    else if (t < 24) return `${t} hours`;
    else if (t == 24) return "1 day";
    else if (t < 168) { const result = Math.round(t / 24); return `${result} day${result == 1 ? '' : 's'}`; }
    else if (t == 168) return `1 week`;
    else if (t < 720) { const result = Math.round(t / 168); return `${result} week${result == 1 ? '' : 's'}`; }
    else if (t == 720) return "1 month";
    else if (t < 8760) { const result = Math.round(t / 720); return `${result} month${result == 1 ? '' : 's'}`;}
    else if (t == 8760) return "1 year";
    else { const result = Math.round(t / 8760); return `${result} year${result == 1 ? '' : 's'}`;}
}

/** Hours in the nominal year and day used by `formatElapsed`. */
const HOURS_PER_YEAR = 8760;
const HOURS_PER_DAY = 24;

/**
 * Elapsed simulated time as an exact, compact breakdown: "1 y 47 d 8 h".
 *
 * `encodeTime` rounds to one unit, which is right for a setting ("about 3 months") and wrong for
 * a clock - the playhead sits on a particular step and two steps a week apart must not read the
 * same. Zero units are dropped ("1 y 3 h"), and years are the nominal 365 days, so "1 y" is always
 * exactly 8760 h; the calendar date beside it (`formatClock`) is what tracks leap years.
 */
export const formatElapsed = (hours: number) => {
    const total = Math.max(0, Math.round(hours));
    if (total == 0) return "0 h";
    const years = Math.floor(total / HOURS_PER_YEAR);
    const days = Math.floor((total % HOURS_PER_YEAR) / HOURS_PER_DAY);
    const rest = total % HOURS_PER_DAY;
    const parts: string[] = [];
    if (years > 0) parts.push(`${years} y`);
    if (days > 0) parts.push(`${days} d`);
    if (rest > 0) parts.push(`${rest} h`);
    return parts.join(" ");
}

//Building an Intl.DateTimeFormat is far slower than using one, and the playhead can ask for a
//label on every mouse move while the slider is dragged.
const clockFormats = new Map<string, Intl.DateTimeFormat>();

function clockFormat(timeZone: string): Intl.DateTimeFormat {
    let format = clockFormats.get(timeZone);
    if (!format) {
        format = new Intl.DateTimeFormat('en-US', {
            timeZone, hourCycle: 'h23',
            year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit',
        });
        clockFormats.set(timeZone, format);
    }
    return format;
}

/**
 * Wall-clock label "2027-03-14 08:00" of an instant in the given IANA zone.
 *
 * Year-month-day with a 24-hour clock, not the browser's locale: the label is compared against
 * run files and exports, and "03/04/2027" is a different day in different countries. An unknown
 * zone falls back to UTC rather than throwing inside a render.
 */
export const formatClock = (when: Date, timeZone: string) => {
    let parts: Intl.DateTimeFormatPart[];
    try {
        parts = clockFormat(timeZone).formatToParts(when);
    } catch {
        parts = clockFormat('UTC').formatToParts(when);
    }
    const get = (type: string) => parts.find(p => p.type === type)?.value ?? '';
    return `${get('year')}-${get('month')}-${get('day')} ${get('hour')}:${get('minute')}`;
}
