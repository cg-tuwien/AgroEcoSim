import { Signal, batch, effect, signal } from "@preact/signals";
import appstate from "../appstate";
import * as THREE from 'three';
import { neutralColor } from "./Selection";
import { BaseRequestObject, ReqObjMaterials } from "./BaseRequestObject";

const color = new THREE.Color("#999");
const box = new THREE.BoxGeometry().translate(0, 0.5, 0);
const disk = new THREE.CircleGeometry().rotateX(-Math.PI * 0.5);
const defaultMaterial = new THREE.MeshLambertMaterial({
    color: color,
    polygonOffset: true,
    polygonOffsetFactor: -2,
    side: THREE.DoubleSide,
    flatShading: true,
    name: "obstacleDefault"
});
const hoverMaterial = new THREE.MeshLambertMaterial({ ...defaultMaterial, color: color.clone().lerpHSL(neutralColor, 0.1), name: "obstacleHover "});
const selectMaterial = new THREE.MeshLambertMaterial({ ...defaultMaterial, color: color.clone().lerpHSL(neutralColor, 0.2), name: "obstacleSelect" });
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

export type ObstacleType = "wall" | "umbrella" | "mesh";

export class Obstacle extends BaseRequestObject
{
    type: Signal<ObstacleType>;
    angleX: Signal<number>;
    angleY: Signal<number>;
    wallLength_UmbrellaRadius: Signal<number>;
    height: Signal<number>;
    thickness: Signal<number>;

    vertices: Float32Array;
    faces: number[];
    bufferGeometry: THREE.BufferGeometry;

    constructor(type: ObstacleType, x: number, y: number, z: number, ax: number, ay: number, l: number, h: number, t: number, verts: Float32Array, faces: number[]) {
        super(x, y, z, obstacleMaterials);

        this.type = signal(type);
        this.angleX = signal(ax);
        this.angleY = signal(ay);
        this.wallLength_UmbrellaRadius = signal(l);
        this.height= signal(h);
        this.thickness = signal(t);

        this.vertices = verts;
        this.faces = faces;

        switch(type)
        {
            case "wall":
                this.mesh = new THREE.Mesh(box, obstacleMaterials.default);
                this.setupWall();
                break;
            case "umbrella":
                this.mesh = new THREE.Mesh(disk, obstacleMaterials.default);
                this.setupUmbrella();
                break;
            case "mesh":
                // console.log("Vertices", this.vertices, this.vertices.length % 3, this.vertices.length / 3);
                // console.log("Faces", this.faces, this.faces.length % 3, this.faces.length / 3);
                // console.log("MaxVertex", this.faces.reduce((a,c) => Math.max(a, c), 0));
                this.movable = false;
                this.bufferGeometry = new THREE.BufferGeometry();
                this.bufferGeometry.setAttribute('position', new THREE.Float32BufferAttribute(this.vertices, 3));
                this.bufferGeometry.setIndex(this.faces);
                this.bufferGeometry.computeVertexNormals();
                this.bufferGeometry.computeBoundingBox();
                this.bufferGeometry.computeBoundingSphere();
                // this.bufferGeometry.setIndex(new THREE.BufferAttribute(this.faces, 1))
                // this.bufferGeometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array([-20, 0, 20,  20, 0, 20,  -20, 0, -20]), 3));
                // this.bufferGeometry.setIndex([0, 1, 2]);
                // this.bufferGeometry = new THREE.BoxGeometry();
                // this.bufferGeometry.scale(100, 10, 2);
                this.mesh = new THREE.Mesh(this.bufferGeometry, obstacleMaterials.default);
                // this.setupMesh();
                // this.wallLength_UmbrellaRadius.value = 1;
                // this.height.value = 1;
                // this.thickness.value = 1;
                // this.mesh = new THREE.Mesh(box, meshMaterial);
                // this.setupWall();
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
                case "mesh":
                    this.mesh.geometry = this.bufferGeometry;
                    break;
            }
            appstate.needsRender.value = true;
        });

        effect(() => {
            if (this.type.peek() !== "mesh")
            {
                const lr = this.wallLength_UmbrellaRadius.value;
                const t = this.thickness.value;

                this.mesh.scale.set(
                    lr * (this.type.peek() == "wall" ? 1 : 0.5),
                    this.height.value,
                    this.type.peek() == "wall" ? t : lr * 0.5);
            }
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

    private setupMesh() {
        this.mesh.position.set(this.px.peek(), this.py.peek(), this.pz.peek());
    }

    static rndObstacle() {
        const type = Math.random() > 0.5 ? "wall" : "umbrella";
        const isWall = type == "wall";
        return new Obstacle(type,
            Math.random() * appstate.fieldSizeX.value, 0, Math.random() * appstate.fieldSizeZ.value,
            0, 0,
            isWall ? 4 : 1, isWall ? 3 : 2.2,  isWall ? 0.4 : 0.08, undefined, undefined);
    }

    transformMove() {
        if (this.movable)
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
            vt: Array.from(this.vertices),
            fc: this.faces
        }
    }
}