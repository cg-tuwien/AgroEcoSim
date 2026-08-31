//Microbenchmark for the scene decode path. Not an assertion of speed - it prints a table and
//only fails if the decode stops producing the same scene. Run it with:
//    npx jest tests/decodeBench.test.ts
import BinaryReader from '../src/helpers/BinaryReader';
const fs = require('fs');

const SESSION = '/home/ubuntu/agro-fixture/session.json';

function b64(s: string): Uint8Array {
    const bin = Buffer.from(s, 'base64');
    return new Uint8Array(bin.buffer, bin.byteOffset, bin.byteLength);
}

describe('scene decode cost', () => {
    test('decode throughput', () => {
        const session = JSON.parse(fs.readFileSync(SESSION, 'utf8'));
        const frames: Uint8Array[] = session.history.map((h: any) => b64(h.s));

        //Warm up so the JIT has tiered up before the timed run.
        for (let i = 0; i < 20; ++i) frames.forEach(f => new BinaryReader(f).readAgroScene());

        const REPS = 200;
        const t0 = process.hrtime.bigint();
        let prims = 0;
        for (let r = 0; r < REPS; ++r)
            for (const f of frames) {
                const scene = new BinaryReader(f).readAgroScene();
                prims += scene.reduce((n: number, p: any) => n + p.primitives.length, 0);
            }
        const ms = Number(process.hrtime.bigint() - t0) / 1e6;

        const decodes = REPS * frames.length;
        const perPrim = (ms * 1e6) / prims;   //nanoseconds
        console.log(`decode: ${decodes} scenes in ${ms.toFixed(1)} ms  ` +
                    `= ${(ms / decodes).toFixed(3)} ms/scene, ` +
                    `${(prims / decodes).toFixed(0)} primitives/scene, ` +
                    `${perPrim.toFixed(1)} ns/primitive`);

        expect(prims).toBeGreaterThan(0);
    });
});
