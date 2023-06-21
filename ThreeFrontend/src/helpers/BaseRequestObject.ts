import { Signal, batch, signal } from "@preact/signals";
import { SelectionState } from "./Selection";
import appstate from "../appstate";
import THREE from "three";

export type ReqObjMaterials = {
    default: THREE.Material;
    hover: THREE.Material;
    select: THREE.Material;
    grab: THREE.Material;
    selectHover: THREE.Material;
}

export class BaseRequestObject {
    px: Signal<number>;
    py: Signal<number>;
    pz: Signal<number>;
    state: Signal<SelectionState>;
    mesh: THREE.Mesh;
    handleMesh: THREE.Object3D;
    grabOffset: THREE.Vector3;
    respondToMove = false;

    materials: ReqObjMaterials;

    constructor(x: number, y: number, z: number, materials: ReqObjMaterials) {
        this.px = signal(x);
        this.py = signal(y);
        this.pz = signal(z);
        this.state = signal("none");
        this.materials = materials;
    }

    hover() {
        this.mesh.material = this.materials.hover;
        this.state.value = "hover";
        appstate.needsRender.value = true;
    }

    unhover() {
        this.mesh.material = this.materials.default;
        this.state.value = "none";

        //this.mesh.remove(this.handleMesh);
        if (appstate.transformControls?.object == this.mesh)
            appstate.transformControls.detach();
        appstate.transformControls.removeEventListener('change', this.transformMoveEvent);

        appstate.needsRender.value = true;
    }

    select() {
        this.mesh.material = this.materials.select;
        this.state.value = "select";
        appstate.needsRender.value = true;
    }

    selecthover() {
        this.mesh.material = this.materials.selectHover;
        this.state.value = "selecthover";

        //check https://threejs.org/docs/#examples/en/controls/TransformControls
        // if (!this.handleMesh)
        // {
        //     this.handleMesh = new THREE.Object3D();
        //     const length = 0.15;
        //     const headLength = 0.2 * length;
        //     const headWidth = 0.7 * headLength;
        //     this.handleMesh.add( new THREE.ArrowHelper(new THREE.Vector3(1, 0, 0), new THREE.Vector3(0, 0, 0), length, "#b00", headLength, headWidth ) );
        //     this.handleMesh.add( new THREE.ArrowHelper(new THREE.Vector3(0, 1, 0), new THREE.Vector3(0, 0, 0), length, "#0b0", headLength, headWidth ) );
        //     this.handleMesh.add( new THREE.ArrowHelper(new THREE.Vector3(0, 0, 1), new THREE.Vector3(0, 0, 0), length, "#00b", headLength, headWidth ) );
        // }

        // this.mesh.add(this.handleMesh);
        // if (appstate.transformControls?.object)
        //     appstate.transformControls.detach();
        appstate.transformControls?.attach(this.mesh);
        appstate.transformControls?.addEventListener('change', this.transformMoveEvent);

        appstate.needsRender.value = true;
    }

    transformMove() {
        batch(() => {
            this.px.value = this.mesh.position.x;
            this.py.value = this.mesh.position.y;
            this.pz.value = this.mesh.position.z;
            appstate.needsRender.value = true;
        });
    }

    transformMoveEvent = this.transformMove.bind(this);

    grab(raycaster: THREE.Raycaster) {
        if (this.respondToMove)
        {
            const plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), -this.py.value);
            const start = new THREE.Vector3();
            raycaster.ray.intersectPlane(plane, start);
            this.mesh.material = this.materials.grab;
            this.state.value = "grab";
            this.grabOffset = new THREE.Vector3(this.px.value, this.py.value, this.pz.value).sub(start);
            appstate.needsRender.value = true;
        }
    }

    move(raycaster: THREE.Raycaster) {
        if (this.respondToMove)
        {
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
    }

    ungrab(s: SelectionState) {
        if (this.respondToMove)
        {
            this.mesh.material = s == "select" ? this.materials.select : this.materials.selectHover;
            this.state.value = s;
            appstate.needsRender.value = true;
        }
    }
}
