import * as THREE from 'three';
import { BaseRequestObject, ReqObjMaterials } from "./BaseRequestObject";
import { neutralColor } from "./Selection";

const color = new THREE.Color("#573600");
const defaultMaterial = new THREE.MeshLambertMaterial({
    color: color,
    side: THREE.DoubleSide,
    flatShading: true,
    name: "terrainDefault"
});
const hoverMaterial = new THREE.MeshLambertMaterial({ ...defaultMaterial, color: color.clone().lerpHSL(neutralColor, 0.1), name: "terrainHover "});
const selectMaterial = new THREE.MeshLambertMaterial({ ...defaultMaterial, color: color.clone().lerpHSL(neutralColor, 0.2), name: "terrainSelect" });
const grabMaterial = selectMaterial;
const selectHoverMaterial = selectMaterial;

const terrainMaterials : ReqObjMaterials = {
    default: defaultMaterial,
    hover: hoverMaterial,
    select: selectMaterial,
    grab: grabMaterial,
    selectHover: selectHoverMaterial
}

export const terrainDefaultMaterial = defaultMaterial;

export abstract class ITerrainItem extends BaseRequestObject {
    abstract save(): void;
    id: string;
    abstract posx(): number;
    abstract posy(): number;
    abstract posz(): number;
    abstract sx(): number;
    abstract sy(): number;
    abstract sz(): number;
}

export class BoxTerrainItem extends ITerrainItem {
    data: Float32Array;

    constructor(id: string, data: Float32Array, index: number) {
        super(data[0], data[1], data[2], terrainMaterials, index);
        this.data = data;
        this.id = id;
        this.movable = false;
    }

    posx = () => this.data[0];
    posy = () => this.data[1];
    posz = () => this.data[2];

    sx = () => this.data[3];
    sy = () => this.data[4];
    sz = () => this.data[5];

    qx = () => this.data[6];
    qy = () => this.data[7];
    qz = () => this.data[8];
    qw = () => this.data[9];

    save() {
        return {"id": this.id, "data": Array.from(this.data) }
    }

    static load(input: any, index: number) {
        const typedArray = new Float32Array(input.data);
        return new BoxTerrainItem(input.id, typedArray, index);
    }
}

export class MeshTerrainItem extends ITerrainItem {
    points: Float32Array;
    triangles: number[];
    pos: Float32Array;
    principalDir: THREE.Vector2;
    secondaryDir: THREE.Vector2;
    principalSize: number;
    secondarySize: number;

    constructor(id: string, x: number, y: number, z: number, points: Float32Array, triangles: number[], index: number) {
        super(x, y, z, terrainMaterials, index);
        this.id = id;
        this.movable = false;
        this.points = points;
        this.triangles = triangles;

        this.principalDir = this.principalDirection().normalize();
        this.secondaryDir = new THREE.Vector2(-this.principalDir.y, this.principalDir.x);

        this.principalSize = this.computeExtent(this.principalDir);
        this.secondarySize = this.computeExtent(this.secondaryDir);
    }

    principalDirection() {
        const n = this.points.length / 3;
        if (n < 2) throw new Error("Need at least 2 points");

        // --- 1. Compute mean of X and Z ---
        let meanX = 0, meanZ = 0;
        for (let i = 0; i < this.points.length; i += 3) {
            meanX += this.points[i]; meanZ += this.points[i + 2];
        }

        meanX /= n; meanZ /= n;

        // --- 2. Compute covariance matrix elements for XZ plane ---
        let covXX = 0, covXZ = 0, covZZ = 0;
        for (let i = 0; i < this.points.length; i += 3) {
            const x = this.points[i] - meanX;
            const z = this.points[i + 2] - meanZ;

            covXX += x * x;
            covXZ += x * z;
            covZZ += z * z;
        }

        covXX /= n;
        covXZ /= n;
        covZZ /= n;

        // --- 3. Solve eigenvector of 2×2 covariance matrix ---
        // | covXX covXZ |
        // | covXZ covZZ |

        const trace = covXX + covZZ;
        const det = covXX * covZZ - covXZ * covXZ;
        //the largest eigenvalue is the one with the + sign
        const lambda = trace / 2 + Math.sqrt((trace * trace) / 4 - det);

        // eigenvector for the largest eigenvalue
        let vx = covXZ;
        let vz = lambda - covXX;

        // Handle degenerate case
        if (Math.abs(vx) < 1e-6 && Math.abs(vz) < 1e-6) {
            vx = 1;
            vz = 0;
        }

        return vx > 0 ? new THREE.Vector2(vx, vz) : new THREE.Vector2(-vx, -vz);
    }

    computeExtent(direction: THREE.Vector2) {
        let min = Infinity;
        let max = -Infinity;
        for(let i = 0; i < this.points.length; i += 3) {
            //project onto the direction vector
            const proj = this.points[i] * direction.x + this.points[i + 2] * direction.y;
            if (proj < min) min = proj;
            if (proj > max) max = proj;
        }

        return max - min;
    }

    save() {
        return {"id": this.id, px: this.posx(), py: this.posy(), pz: this.posz(), "points": Array.from(this.points), "triangles": this.triangles }
    }

    static load(input: any, index: number) {
        const points = new Float32Array(input.points);
        return new MeshTerrainItem(input.id, input.px, input.py, input.pz, points, input.triangles, index);
    }

    sx() {
        let min = this.points[0], max = this.points[0];
        for(let i = 3; i < this.points.length; i += 3)
        {
            if (this.points[i] < min) min = this.points[i];
            if (this.points[i] > max) max = this.points[i];
        }
        return max - min;
    }

    sy() {
        let min = this.points[1], max = this.points[1];
        for(let i = 4; i < this.points.length; i += 3)
        {
            if (this.points[i] < min) min = this.points[i];
            if (this.points[i] > max) max = this.points[i];
        }
        return max - min;
    }

    sz() {
        let min = this.points[2], max = this.points[2];
        for(let i = 5; i < this.points.length; i += 3)
        {
            if (this.points[i] < min) min = this.points[i];
            if (this.points[i] > max) max = this.points[i];
        }
        return max - min;
    }

    posx() { return this.px.value; }
    posy() { return this.py.value; }
    posz() { return this.pz.value; }
}