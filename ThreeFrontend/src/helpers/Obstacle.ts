import { Signal, signal } from "@preact/signals";
import { BaseRequestObject, Seed } from "./Seed";
import appstate from "../appstate";
import * as THREE from 'three';

export type ObstacleType = "wall" | "umbrella";

export class Obstacle extends BaseRequestObject
{
    type: Signal<ObstacleType>;
    angleX: Signal<number>;
    angleY: Signal<number>;
    wallLength_UmbrellaRadius: Signal<number>;
    height: Signal<number>;
    thickness: Signal<number>;

    constructor(type: ObstacleType, x: number, y: number, z: number, ax: number, ay: number, l: number, h: number, t: number) {
        super();
        this.px = signal(x);
        this.py = signal(y);
        this.pz = signal(z);
        this.type = signal(type);
        this.angleX = signal(ax);
        this.angleY = signal(ay);
        this.wallLength_UmbrellaRadius = signal(l);
        this.height= signal(h);
        this.thickness = signal(t);
        this.state = signal("none");

        // this.mesh = new THREE.Mesh(dodecahedron, SeedMaterial)

        // result.mesh.position.set(x, y, z);
        // result.mesh.userData = { type: "seed", seed: result };
        // appstate.threescene.add(this.mesh);
    }

    static rndObstacle() {
        const type = Math.random() > 0.5 ? "wall" : "umbrella";
        const isWall = type == "wall";
        return new Obstacle(type,
            Math.random() * appstate.fieldSizeX.value, 0, Math.random() * appstate.fieldSizeZ.value,
            0, 0,
            isWall ? 4 : 1, isWall ? 3 : 2.2,  isWall ? 0.4 : 0.08);
    }

    static debugWall(): Obstacle {
        return new Obstacle("wall",
            0, 0, -0.2,
            0, 0,
            3, 2.2, 0.4);
    }
}