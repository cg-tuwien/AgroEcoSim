import { Signal, effect, signal } from "@preact/signals";
import { BaseRequestObject, Seed } from "./Seed";
import appstate from "../appstate";
import * as THREE from 'three';
import { neutralColor } from "./Selection";

const color = new THREE.Color("#a60");
const box = new THREE.BoxGeometry().translate(0, 0.5, 0);
const disk = new THREE.CircleGeometry().rotateX(-Math.PI * 0.5);
const defaultMaterial = new THREE.MeshLambertMaterial({
    color: color,
    polygonOffset: true,
    polygonOffsetFactor: -2,
    side: THREE.DoubleSide
});
const hoverMaterial = new THREE.MeshStandardMaterial({ color: color.clone().lerp(neutralColor, 0.1) });
const selectMaterial = new THREE.MeshStandardMaterial({ color: color.clone().lerp(neutralColor, 0.25) });
const grabMaterial = new THREE.MeshStandardMaterial({ color: color.clone().lerp(new THREE.Color("#900"), 0.5) });
const selectHoverMaterial = new THREE.MeshStandardMaterial({ color: grabMaterial.color.clone().lerp(selectMaterial.color, 0.5) });

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
        super(x, y, z);

        this.type = signal(type);
        this.angleX = signal(ax);
        this.angleY = signal(ay);
        this.wallLength_UmbrellaRadius = signal(l);
        this.height= signal(h);
        this.thickness = signal(t);

        switch(type)
        {
            case "wall":
                this.mesh = new THREE.Mesh(box, defaultMaterial);
                this.setupWall();
                break;
            case "umbrella":
                this.mesh = new THREE.Mesh(disk, defaultMaterial);
                this.setupUmbrella();
                break;
        }

        appstate.objObstacles.add(this.mesh);
        appstate.needsRender.value = true;

        effect(() => {
            this.mesh.position.set(this.px.value, this.py.value + (this.type.peek() == "wall" ? 0 : this.height.value), this.pz.value);
            appstate.needsRender.value = true;
        });

        effect(() => {
            switch(this.type.value)
            {
                case "wall":
                    this.mesh.geometry = box;
                    this.setupWall();
                    break;
                case "umbrella":
                    this.mesh.geometry = disk;
                    this.setupUmbrella();
                    break;
            }
            appstate.needsRender.value = true;
        });

        effect(() => {
            const lr = this.wallLength_UmbrellaRadius.value;
            const t = this.thickness.value;
            this.mesh.scale.set(
                lr * (this.type.peek() == "wall" ? 1 : 0.5),
                this.height.value,
                this.type.peek() == "wall" ? t : lr * 0.5);
            appstate.needsRender.value = true;
        })
    }

    private setupUmbrella() {
        this.mesh.userData = { type: "umbrella", umbrella: this };
        this.mesh.scale.set(this.wallLength_UmbrellaRadius.peek() * 0.5, this.height.peek(), this.wallLength_UmbrellaRadius.peek() * 0.5);
        this.mesh.position.set(this.px.peek(), this.py.peek() + this.height.peek(), this.pz.peek());
    }

    private setupWall() {
        this.mesh.userData = { type: "wall", wall: this };
        this.mesh.scale.set(this.wallLength_UmbrellaRadius.peek(), this.height.peek(), this.thickness.peek());
        this.mesh.position.set(this.px.peek(), this.py.peek(), this.pz.peek());
    }

    static rndObstacle() {
        const type = Math.random() > 0.5 ? "wall" : "umbrella";
        const isWall = type == "wall";
        return new Obstacle(type,
            Math.random() * appstate.fieldSizeX.value, 0, Math.random() * appstate.fieldSizeZ.value,
            0, 0,
            isWall ? 4 : 1, isWall ? 3 : 2.2,  isWall ? 0.4 : 0.08);
    }
}