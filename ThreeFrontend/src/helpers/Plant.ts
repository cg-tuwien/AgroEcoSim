//Three.js adapter over the renderer-neutral colour core.
//
//All colour decisions now live in src/render/core (Color, Ramps, Palette, PlantColors) so
//the WebGPU renderer produces identical values without duplicating the logic. What remains
//here is exactly the Three-specific part: allocating and mutating materials and meshes.
//
//The exported signatures are unchanged so ThreeSceneFn.tsx keeps working untouched.

import * as THREE from 'three';
import appstate from "../appstate";
import { threeBoxPrimitive, threeCylinderPrimitive, threePlanePrimitive, threeSpherePrimitive, debugBoxPrimitive } from "../render/three/ThreePrimitives";
import { Primitive, Primitives } from "./Primitives";
import { Index } from "./Scene";
import { OrganColor, leafColor, stemColor, rootColor, budColor, VisualMappingOptions } from "../render/core/PlantColors";
import { neutral as neutralRGB } from "../render/core/Theme";
import { toThreeColor } from "../render/three/ThreeColor";

//VisualMappingOptions now lives in the neutral core and is imported directly by appstate.ts
//and the HUD, which also breaks the appstate <-> Plant import cycle that used to exist.

type ThreeOrganColor = { color: THREE.Color, emissive: THREE.Color };

function toThree(c: OrganColor): ThreeOrganColor {
    return { color: toThreeColor(c.color), emissive: toThreeColor(c.emissive) };
}

const mapping = () => appstate.visualMapping.peek() as VisualMappingOptions;

function LeafColor(primitive: Primitive, index: number): ThreeOrganColor {
    return toThree(leafColor(primitive, index, mapping()));
}

function StemColor(primitive: Primitive): ThreeOrganColor {
    return toThree(stemColor(primitive, mapping()));
}

function RootColor(primitive: Primitive): ThreeOrganColor {
    return toThree(rootColor(primitive, mapping()));
}

function BudColor(primitive: Primitive): ThreeOrganColor {
    return toThree(budColor(primitive, mapping()));
}

export function EncodePlantName(index: Index) {
    return `${index.entity.toString()}_${index.primitive.toString()}`;
}

export function DecodePlantName(name: string) : Index {
    const a = name.split("_");
    return { entity: parseInt(a[0]), primitive: parseInt(a[1])};
}

export function SetupMesh(primitive: Primitive, index: Index, mesh: THREE.Mesh) {
    if (appstate.debugBoxes.value)
    {
        mesh.geometry = debugGeometry;
        mesh.material = debugMaterial;
    }

    switch(primitive.type)
    {
        case Primitives.Rectangle: mesh.visible = appstate.showLeaves.value; break;
        case Primitives.Box: mesh.visible = appstate.showRoots.value; break;
        default: mesh.visible = true;
    }

    mesh.name = EncodePlantName(index);
    mesh.userData = { type: "plant", index: index };

    return mesh;
}

export function CreateLeafMesh(primitive: Primitive, leafGeometry: THREE.BufferGeometry, index: Index) {
    const material = primitive.stats ? new THREE.MeshStandardMaterial({ ...LeafColor(primitive, index.primitive), side: THREE.DoubleSide }) : doubleGreyMaterial;
    return SetupMesh(primitive, index, new THREE.Mesh(leafGeometry ?? threePlanePrimitive, material));
}

export function UpdateLeafMesh(mesh: THREE.Mesh, primitive: Primitive, leafGeometry: THREE.BufferGeometry, index: Index) {
    mesh.geometry = leafGeometry ?? threePlanePrimitive;
    const material = mesh.material as THREE.MeshStandardMaterial;
    //A named material is one of the shared singletons, so it must not be mutated in place.
    if (material.name)
        mesh.material = new THREE.MeshStandardMaterial({ ...LeafColor(primitive, index.primitive), side: THREE.DoubleSide });
    else
    {
        const c = LeafColor(primitive, index.primitive);
        material.color = c.color;
        material.emissive = c.emissive;
        material.side = THREE.DoubleSide;
        material.needsUpdate = true;
    }

    return SetupMesh(primitive, index, mesh);
}

export function VisualizeLeafMesh(material: THREE.MeshStandardMaterial, index: Index) {
    const primitive = appstate.scene.peek()[index.entity].primitives[index.primitive];
    const c = LeafColor(primitive, index.primitive);
    material.color = c.color;
    material.emissive = c.emissive;
}

export function CreateStemMesh(primitive: Primitive, index: Index) {
    const material = primitive.stats ? new THREE.MeshStandardMaterial(StemColor(primitive)) : singleGreyMaterial;
    return SetupMesh(primitive, index, new THREE.Mesh(threeCylinderPrimitive, material));
}

export function UpdateStemMesh(mesh: THREE.Mesh, primitive: Primitive, index: Index) {
    mesh.geometry = threeCylinderPrimitive;
    const material = mesh.material as THREE.MeshStandardMaterial;
    if (material.name)
        mesh.material = new THREE.MeshStandardMaterial(StemColor(primitive));
    else
    {
        const c = StemColor(primitive);
        material.color = c.color;
        material.emissive = c.emissive;
        material.side = THREE.FrontSide;
        material.needsUpdate = true;
    }

    return SetupMesh(primitive, index, mesh);
}

export function VisualizeStemMesh(material: THREE.MeshStandardMaterial, index: Index) {
    const primitive = appstate.scene.peek()[index.entity].primitives[index.primitive];
    const c = StemColor(primitive);
    material.color = c.color;
    material.emissive = c.emissive;
    material.needsUpdate = true;
}

export function CreateRootMesh(primitive: Primitive, index: Index) {
    const material = primitive.stats ? new THREE.MeshStandardMaterial(RootColor(primitive)) : singleGreyMaterial;
    return SetupMesh(primitive, index, new THREE.Mesh(threeBoxPrimitive, material));
}

export function UpdateRootMesh(mesh: THREE.Mesh, primitive: Primitive, index: Index) {
    mesh.geometry = threeBoxPrimitive;
    const material = mesh.material as THREE.MeshStandardMaterial;
    if (material.name)
        mesh.material = new THREE.MeshStandardMaterial(RootColor(primitive));
    else
    {
        const c = RootColor(primitive);
        material.color = c.color;
        material.emissive = c.emissive;
        material.side = THREE.FrontSide;
        material.needsUpdate = true;
    }

    return SetupMesh(primitive, index, mesh);
}

export function VisualizeRootMesh(material: THREE.MeshStandardMaterial, index: Index) {
    const primitive = appstate.scene.peek()[index.entity].primitives[index.primitive];
    const c = RootColor(primitive);
    material.color = c.color;
    material.emissive = c.emissive;
    material.needsUpdate = true;
}

export function CreateBudMesh(primitive: Primitive, index: Index) {
    const material = primitive.stats ? new THREE.MeshStandardMaterial(BudColor(primitive)) : singleGreyMaterial;
    return SetupMesh(primitive, index, new THREE.Mesh(threeSpherePrimitive, material));
}

export function UpdateBudMesh(mesh: THREE.Mesh, primitive: Primitive, index: Index) {
    mesh.geometry = threeSpherePrimitive;
    const material = mesh.material as THREE.MeshStandardMaterial;
    if (material.name)
        mesh.material = new THREE.MeshStandardMaterial(BudColor(primitive));
    else
    {
        const c = BudColor(primitive);
        material.color = c.color;
        material.emissive = c.emissive;
        material.side = THREE.FrontSide;
        material.needsUpdate = true;
    }

    return SetupMesh(primitive, index, mesh);
}

export function VisualizeBudMesh(material: THREE.MeshStandardMaterial, index: Index) {
    const primitive = appstate.scene.peek()[index.entity].primitives[index.primitive];
    const c = BudColor(primitive);
    material.color = c.color;
    material.emissive = c.emissive;
    material.needsUpdate = true;
}

const neutral = toThreeColor(neutralRGB);
const singleGreyMaterial = new THREE.MeshStandardMaterial({ color: neutral, name: "singleGrey" });
export const doubleGreyMaterial = new THREE.MeshStandardMaterial({ color: neutral, side: THREE.DoubleSide, name: "doubleGrey" });

//The vertex table moved to src/render/core/DebugBox.ts so the WebGPU renderer can build the
//same box without going through three.
const debugGeometry = debugBoxPrimitive;

const debugMaterial = new THREE.MeshStandardMaterial({ vertexColors: true, name: "debug" });
