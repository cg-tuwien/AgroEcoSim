import { Signal, batch, effect, signal } from "@preact/signals";
import appstate from "../appstate";
import * as THREE from 'three';
import { neutralColor } from "./Selection";
import { BaseRequestObject, ReqObjMaterials } from "./BaseRequestObject";

const color = new THREE.Color("#a60");
const box = new THREE.BoxGeometry().translate(0, 0.5, 0);
const disk = new THREE.CircleGeometry().rotateX(-Math.PI * 0.5);
const defaultMaterial = new THREE.MeshLambertMaterial({
    color: color,
    polygonOffset: true,
    polygonOffsetFactor: -2,
    side: THREE.DoubleSide,
    name: "obstacleDefault"
});
const hoverMaterial = new THREE.MeshStandardMaterial({ color: color.clone().lerpHSL(neutralColor, 0.1), name: "obstacleHover "});
const selectMaterial = new THREE.MeshStandardMaterial({ color: color.clone().lerpHSL(neutralColor, 0.2), name: "obstacleSelect" });
//disabled when using the gizmo
//const grabMaterial = new THREE.MeshStandardMaterial({ color: color.clone().lerpHSL(new THREE.Color("#06a"), 0.5) });
//const selectHoverMaterial = new THREE.MeshStandardMaterial({ color: grabMaterial.color.clone().lerpHSL(selectMaterial.color, 0.5) });
const grabMaterial = selectMaterial;
const selectHoverMaterial = selectMaterial;

const obstacleMaterials : ReqObjMaterials = {
    default: defaultMaterial,
    hover: hoverMaterial,
    select: selectMaterial,
    grab: grabMaterial,
    selectHover: selectHoverMaterial
}

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
        super(x, y, z, obstacleMaterials);

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

        this.mesh.userData = { type: "obstacle", obstacle: this };
        appstate.objObstacles.add(this.mesh);
        appstate.needsRender.value = true;

        // effect(() => {
        //     this.mesh.position.set(this.px.value, this.py.value + (this.type.peek() == "wall" ? 0 : this.height.value), this.pz.value);
        //     appstate.needsRender.value = true;
        // });

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
        this.mesh.scale.set(this.wallLength_UmbrellaRadius.peek() * 0.5, this.height.peek(), this.wallLength_UmbrellaRadius.peek() * 0.5);
        this.mesh.position.set(this.px.peek(), this.py.peek() + this.height.peek(), this.pz.peek());
    }

    private setupWall() {
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

    transformMove() {
        batch(() => {
            this.px.value = this.mesh.position.x;
            this.py.value = this.mesh.position.y - (this.type.peek() == "wall" ? 0 : this.height.peek());
            this.pz.value = this.mesh.position.z;
            appstate.needsRender.value = true;
        });
    }

    public save() {
        return {
            px: this.px.peek(),
            py: this.py.peek(),
            pz: this.pz.peek(),
            type: this.type.peek(),
            l: this.wallLength_UmbrellaRadius.peek(),
            h: this.height.peek(),
            t: this.thickness.peek(),
            ax: this.angleX.peek(),
            ay: this.angleY.peek(),
        }
    }
}