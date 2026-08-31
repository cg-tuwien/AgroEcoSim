import { Signal, signal } from "@preact/signals"
import appstate from "../appstate";
import { BaseRequestObject } from "./BaseRequestObject";
import { MeshTerrainItem } from "./Terrain";
import { Species } from "./Species";
import { makeSelectionPalette } from "../render/core/SelectionPalette";
import { Vec3, distanceSq, vec3 } from "../render/core/Vec3";

const seedPalette = makeSelectionPalette("#008");

export class Seed extends BaseRequestObject
{
    species: Signal<string>;
    constructor(spec: string, x: number, y: number, z: number, fieldIndex: number, globalCoordinates: boolean) {
        if (appstate.terrainList[fieldIndex] instanceof MeshTerrainItem && globalCoordinates)
        {
            y += appstate.terrainList[fieldIndex].sy() - 0.01;

            //the most recent json import has plant position in global coordinates, so let's bring them to local tray coords
            x -= appstate.terrainList[fieldIndex].posx();
            y -= appstate.terrainList[fieldIndex].posy();
            z -= appstate.terrainList[fieldIndex].posz();
        }

        super(x, y, z, seedPalette, fieldIndex);
        this.species = signal(spec);

        appstate.needsRender.value = true;
    }

    /**
     * Where this seed is drawn, i.e. its tray-local position plus the tray's own offset.
     *
     * Reads the signals through `.value` so that a renderer calling this inside an effect
     * re-runs when the seed moves - that is what replaces the per-seed effect that used to
     * push straight into mesh.position.
     *
     * (The original branched on `terrain instanceof BoxTerrainItem` here, but both arms
     * called terrain.posy(), so the branch was a no-op and is dropped.)
     */
    worldPosition(): Vec3 {
        const p = vec3(this.px.value, this.py.value, this.pz.value);
        if (appstate.terrainList?.length > 0)
        {
            const terrain = appstate.terrainList[this.fieldIndex.value];
            if (terrain)
                return vec3(p.x + terrain.posx(), p.y + terrain.posy(), p.z + terrain.posz());
        }
        return p;
    }

    public save() {
        return {
            species: this.species.peek(),
            px: this.px.peek(),
            py: this.py.peek(),
            pz: this.pz.peek(),
            fi: this.fieldIndex.peek()
        };
    }

    static rndItem(species: Species[], minDist?: number, fieldIndex?: number) {
        const maxDepth = 0.04;
        const limtedRnd = (padding: number) => {
            return padding + Math.random() * (1 - 2 * padding);
        };
        let fieldSize : Vec3;
        if (appstate.terrainList?.length > 1)
        {
            if (!fieldIndex || fieldIndex < 0 || fieldIndex >= appstate.terrainList?.length) //select a random index if none or a negative or a too large one was specified
                fieldIndex = Math.floor(Math.random() * appstate.terrainList.length);

            fieldSize = vec3(appstate.terrainList[fieldIndex].sx(), appstate.terrainList[fieldIndex].sy(), appstate.terrainList[fieldIndex].sz());
        }
        else
            fieldSize = vec3(appstate.fieldSizeX.value, appstate.fieldSizeD.value, appstate.fieldSizeZ.value);

        let pos = vec3(limtedRnd(0.1) * fieldSize.x, -Math.random() * Math.min(maxDepth, fieldSize.y), limtedRnd(0.1) * fieldSize.z);
        let [bestPos, bestIsolation] = [pos, 0];
        if (minDist > 0)
        {
            let i = 0;
            let dist = Seed.checkDist(fieldSize, pos, fieldIndex);
            while (dist < minDist && i < 16)
            {
                ++i;
                if (dist > bestIsolation)
                {
                    bestPos = pos;
                    bestIsolation = dist;
                }
                pos = vec3(limtedRnd(0.1) * fieldSize.x, -Math.random() * Math.min(maxDepth, fieldSize.y), limtedRnd(0.1) * fieldSize.z);
                dist = Seed.checkDist(fieldSize, pos, fieldIndex);
            }

            if (dist < minDist)
                pos = bestPos;
        }

        if (appstate.terrainList?.length > 0)
            pos = vec3(pos.x, pos.y + fieldSize.y, pos.z);

        return new Seed(species[Math.floor(Math.random() * species.length)].name.peek(), pos.x, pos.y, pos.z, fieldIndex ?? 0, false);
    }

    static checkDist(fieldSize: Vec3, pos: Vec3, fieldIndex: number) {
        //Seeded with the field diagonal so an empty field reports "maximally isolated".
        let min = fieldSize.x * fieldSize.x + fieldSize.y * fieldSize.y + fieldSize.z * fieldSize.z;
        const seeds = appstate.seeds.value;
        for(let i = 0; i < seeds.length; ++i)
            if (seeds[i].fieldIndex.peek() === fieldIndex)
            {
                const d = distanceSq(vec3(seeds[i].px.peek(), seeds[i].py.peek(), seeds[i].pz.peek()), pos);
                if (d < min)
                    min = d;
            }
        return min;
    }
}
