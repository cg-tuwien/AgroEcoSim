//WGSL is shipped as TypeScript template literals, which works only because WGSL contains
//neither a backtick nor `${`. That is a real constraint, not a convention: a backtick in a
//comment inside a shader ends the literal and produces a wall of parse errors far from the
//cause. It has already happened once.

import * as mesh from '../src/render/webgpu/shaders/mesh.wgsl';
import * as voxel from '../src/render/webgpu/gi/shaders/voxel.wgsl';
import * as voxelDebug from '../src/render/webgpu/gi/shaders/voxelDebug.wgsl';
import * as coneTrace from '../src/render/webgpu/gi/shaders/coneTrace.wgsl';

const modules = { mesh, voxel, voxelDebug, coneTrace };

const shaders: [string, string][] = Object.entries(modules).flatMap(([name, m]) =>
    Object.entries(m)
        .filter((e): e is [string, string] => typeof e[1] === 'string')
        .map(([k, v]) => [`${name}.${k}`, v] as [string, string]));

describe('WGSL sources', () => {
    test('there are shaders to check', () => {
        expect(shaders.length).toBeGreaterThan(8);
    });

    test.each(shaders)('%s stays embeddable in a template literal', (_name, src) => {
        expect(src).not.toContain('`');
        expect(src).not.toContain('${');
    });

    //Tint rejects the whole module on a reserved word, and the only symptom is a validation
    //error naming a line number in a string that has no line numbers in the editor.
    const RESERVED = ['target', 'sample', 'filter', 'mat', 'vec', 'texture', 'binding', 'input', 'output'];
    test.each(shaders)('%s declares no variable named after a WGSL reserved word', (_name, src) => {
        for (const word of RESERVED) {
            expect(src).not.toMatch(new RegExp(`\\bvar(<[^>]*>)?\\s+${word}\\s*:`));
            expect(src).not.toMatch(new RegExp(`\\blet\\s+${word}\\s*[:=]`));
            expect(src).not.toMatch(new RegExp(`\\bfn\\s+${word}\\s*\\(`));
        }
    });
});
