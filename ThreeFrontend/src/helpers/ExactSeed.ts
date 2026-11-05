/** Decimal text preserves all 64 seed bits through JSON and HTML inputs. */
export function exactSeed(value: string | number): string {
    if (typeof value === 'number' && (!Number.isSafeInteger(value) || value < 0))
        throw new Error('The seed must be an exact unsigned 64-bit integer.');
    const text = String(value);
    if (!/^\d+$/.test(text)) throw new Error('The seed must contain decimal digits only.');
    const normalized = text.replace(/^0+(?=\d)/, '');
    const max = '18446744073709551615';
    if (normalized.length > max.length || (normalized.length === max.length && normalized > max))
        throw new Error('The seed exceeds the unsigned 64-bit range.');
    return normalized;
}

/** Tokenize before JSON.parse: a reviver runs after integer precision has already been lost.
 * Strings are consumed whole, so seed-looking text inside a string cannot be rewritten. */
export function parseSeedJson(text: string): any {
    let previous = '', key = '';
    const protectedText = text.replace(/"(?:\\.|[^"\\])*"|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?|[^\s]/g, token => {
        let result = token;
        if (previous === ':' && (key === 'Seed' || key === 'seed' || key === 'initNumber') && /^\d+$/.test(token))
            result = JSON.stringify(exactSeed(token));
        if (token.startsWith('"')) key = JSON.parse(token);
        previous = token;
        return result;
    });
    return JSON.parse(protectedText);
}
