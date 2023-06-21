import { effect } from "@preact/signals"
import * as THREE from 'three';
import { neutralColor } from "./Selection";
import appstate from "../appstate";
import { BaseRequestObject, ReqObjMaterials } from "./BaseRequestObject";

const seedColor = new THREE.Color("#008");
const dodecahedron = new THREE.DodecahedronGeometry(0.03);
const defaultMaterial = new THREE.MeshLambertMaterial({ color: seedColor});
const hoverMaterial = new THREE.MeshLambertMaterial({ color: seedColor.clone().lerpHSL(neutralColor, 0.1) });
const selectMaterial = new THREE.MeshLambertMaterial({ color: seedColor.clone().lerpHSL(neutralColor, 0.2) });
//disabled when using the gizmo
// const grabMaterial = new THREE.MeshLambertMaterial({ color: seedColor.clone().lerpHSL(new THREE.Color("#900"), 0.5) });
// const selectHoverMaterial = new THREE.MeshLambertMaterial({ color: grabMaterial.color.clone().lerpHSL(selectMaterial.color, 0.5) });
const grabMaterial = selectMaterial;
const selectHoverMaterial = selectMaterial;

// const seedMaterials : ReqObjMaterials = {
//     default: defaultMaterial,
//     hover: hoverMaterial,
//     select: selectMaterial,
//     grab: grabMaterial,
//     selectHover: selectHoverMaterial
// }

// export class Plant
// {
//     constructor(x: number, y: number, z: number) {

//         this.mesh = new THREE.Mesh(dodecahedron, defaultMaterial);

//         this.mesh.position.set(x, y, z);
//         this.mesh.userData = { type: "seed", seed: this };
//         //this.mesh.layers.set(1);
//         appstate.objSeeds.add(this.mesh);
//         appstate.needsRender.value = true;

//         effect(() => {
//             this.mesh.position.set(this.px.value, this.py.value, this.pz.value);
//             appstate.needsRender.value = true;
//         });
//     }

//     static rndItem() {
//         return new Seed(
//             Math.random() * appstate.fieldSizeX.value,
//             -Math.random() * 0.1,
//             Math.random() * appstate.fieldSizeZ.value);
//     }
// }