import { Signal, effect, signal } from "@preact/signals"
import * as THREE from 'three';
import { neutralColor } from "./Selection";
import appstate from "../appstate";
import { BaseRequestObject, ReqObjMaterials } from "./BaseRequestObject";
import { BoxTerrainItem, MeshTerrainItem } from "./Terrain";
import { Species } from "./Species";

const seedColor = new THREE.Color("#008");
const dodecahedron = new THREE.DodecahedronGeometry(0.03);
const defaultMaterial = new THREE.MeshLambertMaterial({ color: seedColor, name: "seedDefault" });
const hoverMaterial = new THREE.MeshLambertMaterial({ color: seedColor.clone().lerpHSL(neutralColor, 0.1), name: "seedHover" });
const selectMaterial = new THREE.MeshLambertMaterial({ color: seedColor.clone().lerpHSL(neutralColor, 0.2), name: "seedSelect" });
//disabled when using the gizmo
// const grabMaterial = new THREE.MeshLambertMaterial({ color: seedColor.clone().lerpHSL(new THREE.Color("#900"), 0.5) });
// const selectHoverMaterial = new THREE.MeshLambertMaterial({ color: grabMaterial.color.clone().lerpHSL(selectMaterial.color, 0.5) });
const grabMaterial = selectMaterial;
const selectHoverMaterial = selectMaterial;

const seedMaterials : ReqObjMaterials = {
    default: defaultMaterial,
    hover: hoverMaterial,
    select: selectMaterial,
    grab: grabMaterial,
    selectHover: selectHoverMaterial
}

export class Seed extends BaseRequestObject
{
    species: Signal<string>;
    constructor(spec: string, x: number, y: number, z: number, fieldIndex: number, isLoading: boolean) {
        if (appstate.terrainList[fieldIndex] instanceof MeshTerrainItem && !isLoading)
            y += appstate.terrainList[fieldIndex].sy();

        super(x, y, z, seedMaterials, fieldIndex);
        this.species = signal(spec);

        this.mesh = new THREE.Mesh(dodecahedron, defaultMaterial);

        this.mesh.position.set(x, y, z);
        this.mesh.userData = { type: "seed", seed: this };
        //this.mesh.layers.set(1);
        appstate.objSeeds.add(this.mesh);
        appstate.needsRender.value = true;

        effect(() => {
            const offset = new THREE.Vector3();
            if (appstate.terrainList?.length > 0)
            {
                const terrain = appstate.terrainList[this.fieldIndex.value];
                offset.set(terrain.posx(), terrain instanceof BoxTerrainItem ? terrain.posy() : terrain.posy(), terrain.posz());
            }
            this.mesh.position.set(this.px.value + offset.x, this.py.value + offset.y, this.pz.value + offset.z);
            appstate.needsRender.value = true;
        });
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
        let fieldSize : THREE.Vector3;
        console.log(fieldIndex, minDist, appstate.terrainList?.length);
        if (appstate.terrainList?.length > 1)
        {
            if (!fieldIndex || fieldIndex < 0 || fieldIndex >= appstate.terrainList?.length) //select a random index if none or a negative or a too large one was specified
                fieldIndex = Math.floor(Math.random() * appstate.terrainList.length);

            fieldSize = new THREE.Vector3(appstate.terrainList[fieldIndex].sx(), appstate.terrainList[fieldIndex].sy(), appstate.terrainList[fieldIndex].sz());
        }
        else
            fieldSize = new THREE.Vector3(appstate.fieldSizeX.value, appstate.fieldSizeD.value, appstate.fieldSizeZ.value);
        //console.log(fieldIndex, minDist, appstate.terrainList?.length);

        let pos = new THREE.Vector3(Math.random() * fieldSize.x, -Math.random() * Math.min(0.1, fieldSize.y), Math.random() * fieldSize.z);
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
                pos = new THREE.Vector3(Math.random() * fieldSize.x, -Math.random() * Math.min(0.1, fieldSize.y), Math.random() * fieldSize.z);
                dist = Seed.checkDist(fieldSize, pos, fieldIndex);
            }

            if (dist < minDist)
                pos = bestPos;
        }

        return new Seed(species[Math.floor(Math.random() * species.length)].name.peek(), pos.x, pos.y, pos.z, fieldIndex ?? 0, false);
    }

    static checkDist(fieldSize: THREE.Vector3, pos: THREE.Vector3, fieldIndex: number) {
        let min = fieldSize.lengthSq();
        const seeds = appstate.seeds.value;
        for(let i = 0; i < seeds.length; ++i)
            if (seeds[i].fieldIndex.peek() === fieldIndex)
            {
                const d = new THREE.Vector3(seeds[i].px.peek(), seeds[i].py.peek(), seeds[i].pz.peek()).distanceToSquared(pos);
                if (d < min)
                    min = d;
            }
        return min;
    }
}