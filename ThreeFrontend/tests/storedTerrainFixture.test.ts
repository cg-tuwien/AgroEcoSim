//A terrain exactly as the server stored it, decoded by the reader that has to read it.
//
//The synthetic cases in terrainFormat.test.ts pin the SHAPE; this pins the real bytes, because the
//two ends of this seam are written in different languages and the only thing that has ever caught
//a disagreement between them is running one against the other's output. The fixture is the
//`terrain.bin.gz` of a stored run built from a two-object OBJ - one soil section and one wall - so
//it exercises both halves: the trays a run is planted in and the obstacles it grows against.

import fs from 'fs';
import path from 'path';
import BinaryReader from '../src/helpers/BinaryReader';

const bytes = new Uint8Array(fs.readFileSync(path.join(__dirname, '__fixtures__', 'terrain-facade.bin')));

it('decodes a stored run terrain into its trays and its obstacles', () => {
    const result = new BinaryReader(bytes).readTerrain();

    //One soil section (P1_Y00_S01) - what a seed's fieldIndex indexes into.
    expect(result.terrains.length).toBe(1);
    //The imported obstacles arrive merged into ONE mesh so the viewport keeps a single draw call.
    expect(result.obstacles.length).toBe(1);
    expect(result.obstacles[0].type.peek()).toBe('mesh');
    expect(result.obstacles[0].vertices.length).toBeGreaterThan(0);
    expect(result.obstacles[0].faces.length).toBeGreaterThan(0);

    //And the group table that says which slice of that mesh each named object owns - without it an
    //obstacle cannot be coloured by its own measured leaf area index.
    expect(result.obstacles[0].groups?.length).toBeGreaterThan(0);
});
