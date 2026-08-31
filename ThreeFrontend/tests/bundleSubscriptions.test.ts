//Guards a bug class that has now appeared three times and that no other check catches.
//
//Effects in RendererHost guard on `rendererRef.current`, which is undefined on their first
//run because the renderer mounts asynchronously. Terser sinks single-use locals into their
//use site, so signal reads can end up INSIDE the guarded call - and the guard short-circuits
//before evaluating them. The effect subscribes to nothing and never fires again. It only
//happens in a minified build, so tsc, eslint and every unit test pass while the feature is
//silently dead.
//
//This reads the production bundle and fails if a `.value` read sits after such a guard.
//It is skipped when there is no build, so `npm test` still works on a clean checkout.

import { existsSync, readdirSync, readFileSync } from 'fs';
import { join } from 'path';

const BUILD = join(__dirname, '..', 'build');

function bundles(): string[] {
    if (!existsSync(BUILD)) return [];
    return readdirSync(BUILD)
        .filter(f => /^bundle\..*\.js$/.test(f) && !f.endsWith('.map'))
        .map(f => join(BUILD, f));
}

/** `null===(e=n.current)||void 0===e||` - the guard terser emits for `x.current?.f()`. */
const GUARD = /null===\((\w+)=(\w+)\.current\)\|\|void 0===\1\|\|/g;

/**
 * End of the expression the guard short-circuits, and nothing beyond it.
 *
 * `||` binds tighter than `,`, so in `a||b||c(t),f()` only `c(t)` is guarded and `f()` runs
 * unconditionally. Scanning past that top-level comma reports code that is perfectly fine -
 * the plant-rebuild effect does exactly this to poke the playback loop. So the scan stops at
 * the first `,`, `;` or closing bracket seen at nesting depth zero.
 */
function guardedEnd(src: string, from: number): number {
    let depth = 0;
    for (let i = from; i < src.length; ++i) {
        const c = src[i];
        if (c === '"' || c === "'" || c === '`') {
            //Skip the literal; bundle strings contain brackets that would unbalance the count.
            const quote = c;
            for (++i; i < src.length; ++i) {
                if (src[i] === '\\') { ++i; continue; }
                if (src[i] === quote) break;
            }
            continue;
        }
        if (c === '(' || c === '[' || c === '{') { ++depth; continue; }
        if (c === ')' || c === ']' || c === '}') {
            if (depth === 0) return i;
            --depth;
            continue;
        }
        if (depth === 0 && (c === ',' || c === ';')) return i;
    }
    return src.length;
}

describe('production bundle keeps its signal subscriptions', () => {
    const files = bundles();

    (files.length ? test : test.skip)('no signal is read inside a renderer-guarded call', () => {
        const offenders: string[] = [];
        for (const file of files) {
            const src = readFileSync(file, 'utf8');
            GUARD.lastIndex = 0;
            let m: RegExpExecArray | null;
            while ((m = GUARD.exec(src)) !== null) {
                const start = m.index + m[0].length;
                const tail = src.slice(start, guardedEnd(src, start));
                if (/\.value\b/.test(tail))
                    offenders.push(`${file.split('/').pop()}: ...${tail.slice(0, 160)}`);
            }
        }
        //Each offender is an effect that will never re-run after its first invocation.
        expect(offenders).toEqual([]);
    });

    (files.length ? test : test.skip)('the sun tracks the playhead', () => {
        //A regression guard for the specific spelling that broke it: reading playPointer for
        //its subscription and calling currentTimestep() separately, written as a comma
        //expression, whose discarded operand terser deletes as dead code.
        const src = files.map(f => readFileSync(f, 'utf8')).join('');
        expect(src).toMatch(/timestepAt\(\w+\.\w+\.playPointer\.value\)/);
    });
});
