import { Signal, batch, effect, signal } from "@preact/signals"
import * as THREE from 'three';
import { SelectionState, neutralColor } from "./Selection";
import appstate from "../appstate";

const seedColor = new THREE.Color("#008");
const dodecahedron = new THREE.DodecahedronGeometry(0.03);
const SeedMaterial = new THREE.MeshStandardMaterial({ color: seedColor});
const SeedHoverMaterial = new THREE.MeshStandardMaterial({ color: seedColor.clone().lerp(neutralColor, 0.1) });
const SeedSelectMaterial = new THREE.MeshStandardMaterial({ color: seedColor.clone().lerp(neutralColor, 0.25) });
const SeedGrabMaterial = new THREE.MeshStandardMaterial({ color: seedColor.clone().lerp(new THREE.Color("#900"), 0.5) });
const SeedSelectHoverMaterial = new THREE.MeshStandardMaterial({ color: SeedGrabMaterial.color.clone().lerp(SeedSelectMaterial.color, 0.5) });

export class BaseRequestObject {
    px: Signal<number>;
    py: Signal<number>;
    pz: Signal<number>;
    state: Signal<SelectionState>;
    mesh: THREE.Mesh;
    handleMesh: THREE.Object3D;
    grabOffset: THREE.Vector3;
}

export class Seed extends BaseRequestObject
{
    constructor(x: number, y: number, z: number) {
        super();
        this.px = signal(x);
        this.py = signal(y);
        this.pz = signal(z);
        this.state = signal("none");
        this.mesh = new THREE.Mesh(dodecahedron, SeedMaterial);

        this.mesh.position.set(x, y, z);
        this.mesh.userData = { type: "seed", seed: this };
        this.mesh.layers.set(1);
        appstate.threescene.add(this.mesh);
        appstate.needsRender.value = true;

        effect(() => {
            this.mesh.position.set(this.px.value, this.py.value, this.pz.value);
            appstate.needsRender.value = true;
        });
    }

    static rndItem() {
        return new Seed(
            Math.random() * appstate.fieldSizeX.value,
            -Math.random() * 0.1,
            Math.random() * appstate.fieldSizeZ.value);
    }

    hover() {
        this.mesh.material = SeedHoverMaterial;
        this.state.value = "hover";
        appstate.needsRender.value = true;
    }

    grab(raycaster: THREE.Raycaster) {
        const plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), -this.py.value);
        const start = new THREE.Vector3();
        raycaster.ray.intersectPlane(plane, start);
        this.mesh.material = SeedGrabMaterial;
        this.state.value = "grab";
        this.grabOffset = new THREE.Vector3(this.px.value, this.py.value, this.pz.value).sub(start);
        appstate.needsRender.value = true;
    }

    move(raycaster: THREE.Raycaster) {
        const plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), -this.py.value);
        const p = new THREE.Vector3();
        raycaster.ray.intersectPlane(plane, p);
        const target = this.grabOffset.clone().add(p);
        //this.mesh.position.set(target.x, target.y, target.z);
        batch(() => {
            this.px.value = target.x;
            this.py.value = target.y;
            this.pz.value = target.z;
            appstate.needsRender.value = true;
        });
    }

    select() {
        this.mesh.material = SeedSelectMaterial;
        this.state.value = "select";
        appstate.needsRender.value = true;
    }

    selecthover() {
        this.mesh.material = SeedSelectHoverMaterial;
        this.state.value = "selecthover";

        if (!this.handleMesh)
        {
            this.handleMesh = new THREE.Object3D();
            const length = 0.15;
            const headLength = 0.2 * length;
            const headWidth = 0.7 * headLength;
            this.handleMesh.add( new THREE.ArrowHelper(new THREE.Vector3(1, 0, 0), new THREE.Vector3(0, 0, 0), length, "#b00", headLength, headWidth ) );
            this.handleMesh.add( new THREE.ArrowHelper(new THREE.Vector3(0, 1, 0), new THREE.Vector3(0, 0, 0), length, "#0b0", headLength, headWidth ) );
            this.handleMesh.add( new THREE.ArrowHelper(new THREE.Vector3(0, 0, 1), new THREE.Vector3(0, 0, 0), length, "#00b", headLength, headWidth ) );
        }

        this.mesh.add(this.handleMesh);
        appstate.needsRender.value = true;
    }

    unhover() {
        this.mesh.material = SeedMaterial;
        this.state.value = "none";

        this.mesh.remove(this.handleMesh);

        appstate.needsRender.value = true;
    }

    ungrab(s: SelectionState) {
        this.mesh.material = s == "select" ? SeedSelectMaterial : SeedSelectHoverMaterial;
        this.state.value = s;
        appstate.needsRender.value = true;
    }
}