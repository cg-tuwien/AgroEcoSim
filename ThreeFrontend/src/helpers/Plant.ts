import * as THREE from 'three';
import { backgroundColor, neutralColor } from "./Selection";
import appstate from "../appstate";
import { threeCylinderPrimitive, threePlanePrimitive, threeSpherePrimitive } from "../components/viewport/ThreeSceneFn";
import { Primitive } from "./Primitives";
import { Index } from "./Scene";

export enum VisualMappingOptions { Natural = "natural", Water = "water", Energy = "energy", Irradiance = "irradiance", Resource = "resource", Production = "production" }

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

    mesh.name = EncodePlantName(index);
    mesh.userData = { type: "plant", index: index, customMaterial: !appstate.debugBoxes.value && primitive.type > 1 };

    return mesh;
}

export function CreateLeafMesh(primitive: Primitive, index: Index) {
    const material = primitive.stats ? new THREE.MeshStandardMaterial({ ...LeafColor(primitive), side: THREE.DoubleSide }) : doubleGreyMaterial;
    return SetupMesh(primitive, index, new THREE.Mesh(threePlanePrimitive, material));
}

export function UpdateLeafMesh(mesh: THREE.Mesh, primitive: Primitive, index: Index) {
    mesh.geometry = threePlanePrimitive;
    if (mesh.userData.customMaterial)
    {
        const material = mesh.material as THREE.MeshStandardMaterial;
        const c = LeafColor(primitive);
        material.color = c.color;
        material.emissive = c.emissive;
        material.side = THREE.DoubleSide;
        material.needsUpdate = true;
    }
    else
        mesh.material = new THREE.MeshStandardMaterial({ ...LeafColor(primitive), side: THREE.DoubleSide });

    return SetupMesh(primitive, index, mesh);
}

export function VisualizeLeafMesh(material: THREE.MeshStandardMaterial, index: Index) {
    const primitive = appstate.scene.peek()[index.entity][index.primitive];
    const c = LeafColor(primitive);
    material.color = c.color;
    material.emissive = c.emissive;
}

export function CreateStemMesh(primitive: Primitive, index: Index) {
    const material = primitive.stats ? new THREE.MeshStandardMaterial(StemColor(primitive)) : singleGreyMaterial;
    return SetupMesh(primitive, index, new THREE.Mesh(threeCylinderPrimitive, material));
}

export function UpdateStemMesh(mesh: THREE.Mesh, primitive: Primitive, index: Index) {
    mesh.geometry = threeCylinderPrimitive;
    if (mesh.userData.customMaterial)
    {
        const material = mesh.material as THREE.MeshStandardMaterial;
        const c = StemColor(primitive);
        material.color = c.color;
        material.emissive = c.emissive;
        material.side = THREE.FrontSide;
        material.needsUpdate = true;
    }
    else
        mesh.material = new THREE.MeshStandardMaterial(StemColor(primitive));

    return SetupMesh(primitive, index, mesh);
}

export function VisualizeStemMesh(material: THREE.MeshStandardMaterial, index: Index) {
    const primitive = appstate.scene.peek()[index.entity][index.primitive];
    const c = StemColor(primitive);
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
    if (mesh.userData.customMaterial)
    {
        const material = mesh.material as THREE.MeshStandardMaterial;
        const c = BudColor(primitive);
        material.color = c.color;
        material.emissive = c.emissive;
        material.side = THREE.FrontSide;
        material.needsUpdate = true;
    }
    else
        mesh.material = new THREE.MeshStandardMaterial(BudColor(primitive));

    return SetupMesh(primitive, index, mesh);
}


export function VisualizeBudMesh(material: THREE.MeshStandardMaterial, index: Index) {
    const primitive = appstate.scene.peek()[index.entity][index.primitive];
    const c = BudColor(primitive);
    material.color = c.color;
    material.emissive = c.emissive;
    material.needsUpdate = true;
}

function LeafColor(primitive: Primitive) : { color: THREE.Color, emissive: THREE.Color } {
    switch (appstate.visualMapping.peek()) {
        case VisualMappingOptions.Natural: return { color: greenColor, emissive: black } ;
        case VisualMappingOptions.Water: return { emissive: HeatColor(primitive.stats[0]), color: black };
        case VisualMappingOptions.Energy: return { emissive: HeatColor(primitive.stats[1]), color: black };
        case VisualMappingOptions.Irradiance: return { emissive: HeatColor(primitive.stats[2] * 1e-3), color: black };
        case VisualMappingOptions.Resource: return { emissive: HeatColor(primitive.stats[3]), color: black };
        case VisualMappingOptions.Production: return { emissive: HeatColor(primitive.stats[4]), color: black };
        default: return { color: neutral, emissive: black };
    }
}

function StemColor(primitive: Primitive) : { color: THREE.Color, emissive: THREE.Color } {
    switch (appstate.visualMapping.peek()) {
        case VisualMappingOptions.Natural: return { color: greenColor.clone().lerpHSL(woodColor, primitive.stats[2]), emissive: black };
        case VisualMappingOptions.Water: return { emissive: HeatColor(primitive.stats[0]), color: black };
        case VisualMappingOptions.Energy: return { emissive: HeatColor(primitive.stats[1]), color: black };
        default: return { color: neutral, emissive: black };
    }
}

function BudColor(primitive: Primitive) : { color: THREE.Color, emissive: THREE.Color } {
    switch (appstate.visualMapping.peek()) {
        case VisualMappingOptions.Natural: return {color: greenColor, emissive: black } ;
        case VisualMappingOptions.Water: return { emissive: HeatColor(primitive.stats[0]), color: black };
        case VisualMappingOptions.Energy: return { emissive: HeatColor(primitive.stats[1]), color: black };
        default: return { color: neutral, emissive: black };
    }
}

function HeatColor(value: number) {
    const p = Math.max(0, Math.min(1, value)) * heatColorsMax;
    const s = Math.floor(p);
    return s == heatColorsMax ? heatColors[heatColorsMax] : heatColors[s].clone().lerpHSL(heatColors[s + 1], p - s);
}

const neutral = new THREE.Color(neutralColor).lerpHSL(new THREE.Color(backgroundColor), 0.1);
const singleGreyMaterial = new THREE.MeshStandardMaterial({ color: neutral});
export const doubleGreyMaterial = new THREE.MeshStandardMaterial({ color: neutral, side: THREE.DoubleSide});

const woodColor = new THREE.Color("#7f4f1f");
const greenColor = new THREE.Color("#009900");

const heatColors = [
    new THREE.Color("#003f5c"),
    new THREE.Color("#58508d"),
    new THREE.Color("#bc5090"),
    new THREE.Color("#ff6361"),
    new THREE.Color("#ffa600"),
];

const heatColorsMax = heatColors.length - 1;

const black = new THREE.Color("#000");

const debugLeftColor = [1, 0.0, 0.1];
const debugRightColor = [1, 0.1, 0.1];
const debugBottomColor = [0.1, 1, 0.1];
const debugTopColor = [0, 1, 0];
const debugFrontColor = [0.1, 0.1, 1];
const debugBackColor = [0, 0, 1];

const boxVerts = [
    // front
    { pos: [-1, -1,  1], norm: [ 0,  0,  1], color: [...debugFrontColor], }, // 0
    { pos: [ 1, -1,  1], norm: [ 0,  0,  1], color: [...debugFrontColor], }, // 1
    { pos: [-1,  1,  1], norm: [ 0,  0,  1], color: [...debugFrontColor], }, // 2
    { pos: [ 1,  1,  1], norm: [ 0,  0,  1], color: [...debugFrontColor], }, // 3
    // right
    { pos: [ 1, -1,  1], norm: [ 1,  0,  0], color: [...debugRightColor], }, // 4
    { pos: [ 1, -1, -1], norm: [ 1,  0,  0], color: [...debugRightColor], }, // 5
    { pos: [ 1,  1,  1], norm: [ 1,  0,  0], color: [...debugRightColor], }, // 6
    { pos: [ 1,  1, -1], norm: [ 1,  0,  0], color: [...debugRightColor], }, // 7
    // back
    { pos: [ 1, -1, -1], norm: [ 0,  0, -1], color: [...debugBackColor], }, // 8
    { pos: [-1, -1, -1], norm: [ 0,  0, -1], color: [...debugBackColor], }, // 9
    { pos: [ 1,  1, -1], norm: [ 0,  0, -1], color: [...debugBackColor], }, // 10
    { pos: [-1,  1, -1], norm: [ 0,  0, -1], color: [...debugBackColor], }, // 11
    // left
    { pos: [-1, -1, -1], norm: [-1,  0,  0], color: [...debugLeftColor], }, // 12
    { pos: [-1, -1,  1], norm: [-1,  0,  0], color: [...debugLeftColor], }, // 13
    { pos: [-1,  1, -1], norm: [-1,  0,  0], color: [...debugLeftColor], }, // 14
    { pos: [-1,  1,  1], norm: [-1,  0,  0], color: [...debugLeftColor], }, // 15
    // top
    { pos: [ 1,  1, -1], norm: [ 0,  1,  0], color: [...debugTopColor], }, // 16
    { pos: [-1,  1, -1], norm: [ 0,  1,  0], color: [...debugTopColor], }, // 17
    { pos: [ 1,  1,  1], norm: [ 0,  1,  0], color: [...debugTopColor], }, // 18
    { pos: [-1,  1,  1], norm: [ 0,  1,  0], color: [...debugTopColor], }, // 19
    // bottom
    { pos: [ 1, -1,  1], norm: [ 0, -1,  0], color: [...debugBottomColor], }, // 20
    { pos: [-1, -1,  1], norm: [ 0, -1,  0], color: [...debugBottomColor], }, // 21
    { pos: [ 1, -1, -1], norm: [ 0, -1,  0], color: [...debugBottomColor], }, // 22
    { pos: [-1, -1, -1], norm: [ 0, -1,  0], color: [...debugBottomColor], }, // 23
  ];

const boxPositions = [];
const boxNormals = [];
const boxColors = [];
for (const vertex of boxVerts) {
    boxPositions.push(...vertex.pos);
    boxNormals.push(...vertex.norm);
    boxColors.push(...vertex.color);
}

  const debugGeometry = new THREE.BufferGeometry();
debugGeometry.setAttribute(
    'position',
    new THREE.BufferAttribute(new Float32Array(boxPositions), 3));
debugGeometry.setAttribute(
    'normal',
    new THREE.BufferAttribute(new Float32Array(boxNormals), 3));
debugGeometry.setAttribute(
    'color',
    new THREE.BufferAttribute(new Float32Array(boxColors), 3));

debugGeometry.setIndex([
   0,  1,  2,   2,  1,  3,  // front
   4,  5,  6,   6,  5,  7,  // right
   8,  9, 10,  10,  9, 11,  // back
  12, 13, 14,  14, 13, 15,  // left
  16, 17, 18,  18, 17, 19,  // top
  20, 21, 22,  22, 21, 23,  // bottom
]);

const debugMaterial = new THREE.MeshStandardMaterial({ vertexColors: true });