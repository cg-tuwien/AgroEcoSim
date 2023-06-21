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
    mesh.name = EncodePlantName(index);
    mesh.userData = { type: "plant", index: index, customMaterial: primitive.type > 1 };
    return mesh;
}

export function CreateLeafMesh(primitive: Primitive, index: Index) {
    const material = primitive.stats ? new THREE.MeshStandardMaterial({ color: LeafColor(primitive), side: THREE.DoubleSide }) : doubleBasicMaterial;
    return SetupMesh(primitive, index, new THREE.Mesh(threePlanePrimitive, material));
}

export function UpdateLeafMesh(mesh: THREE.Mesh, primitive: Primitive, index: Index) {
    mesh.geometry = threePlanePrimitive;
    if (mesh.userData.customMaterial)
    {
        const material = mesh.material as THREE.MeshBasicMaterial;
        material.color = LeafColor(primitive);
        material.side = THREE.DoubleSide;
    }
    else
        mesh.material = new THREE.MeshStandardMaterial({ color: LeafColor(primitive), side: THREE.DoubleSide });

    return SetupMesh(primitive, index, mesh);
}

export function VisualizeLeafMesh(material: THREE.MeshBasicMaterial, index: Index) {
    const primitive = appstate.scene.peek()[index.entity][index.primitive];
    material.color = LeafColor(primitive);
}

export function CreateStemMesh(primitive: Primitive, index: Index) {
    const material = primitive.stats ? new THREE.MeshStandardMaterial({ color: StemColor(primitive) }) : singleBasicMaterial;
    return SetupMesh(primitive, index, new THREE.Mesh(threeCylinderPrimitive, material));
}

export function UpdateStemMesh(mesh: THREE.Mesh, primitive: Primitive, index: Index) {
    mesh.geometry = threeCylinderPrimitive;
    if (mesh.userData.customMaterial)
    {
        const material = mesh.material as THREE.MeshBasicMaterial;
        material.color = StemColor(primitive);
        material.side = THREE.FrontSide;
    }
    else
        mesh.material = new THREE.MeshStandardMaterial({ color: StemColor(primitive) });

    return SetupMesh(primitive, index, mesh);
}

export function VisualizeStemMesh(material: THREE.MeshBasicMaterial, index: Index) {
    const primitive = appstate.scene.peek()[index.entity][index.primitive];
    material.color = StemColor(primitive);
}

export function CreateBudMesh(primitive: Primitive, index: Index) {
    const material = primitive.stats ? new THREE.MeshStandardMaterial({ color: BudColor(primitive) }) : singleBasicMaterial;
    return SetupMesh(primitive, index, new THREE.Mesh(threeSpherePrimitive, material));
}


export function UpdateBudMesh(mesh: THREE.Mesh, primitive: Primitive, index: Index) {
    mesh.geometry = threeSpherePrimitive;
    if (mesh.userData.customMaterial)
    {
        const material = mesh.material as THREE.MeshBasicMaterial;
        material.color = BudColor(primitive);
        material.side = THREE.FrontSide;
    }
    else
        mesh.material = new THREE.MeshStandardMaterial({ color: BudColor(primitive) });

    return SetupMesh(primitive, index, mesh);
}


export function VisualizeBudMesh(material: THREE.MeshBasicMaterial, index: Index) {
    const primitive = appstate.scene.peek()[index.entity][index.primitive];
    material.color = BudColor(primitive);
}

function LeafColor(primitive: Primitive) {
    switch (appstate.visualMapping.peek()) {
        case VisualMappingOptions.Natural: return greenColor;
        case VisualMappingOptions.Water: return HeatColor(primitive.stats[0]);
        case VisualMappingOptions.Energy: return HeatColor(primitive.stats[1]);
        case VisualMappingOptions.Irradiance: return HeatColor(primitive.stats[2]);
        case VisualMappingOptions.Resource: return HeatColor(primitive.stats[3]);
        case VisualMappingOptions.Production: return HeatColor(primitive.stats[4]);
        default: return neutral;
    }
}

function StemColor(primitive: Primitive) {
    switch (appstate.visualMapping.peek()) {
        case VisualMappingOptions.Natural: return greenColor.clone().lerpHSL(woodColor, primitive.stats[2]);
        case VisualMappingOptions.Water: return HeatColor(primitive.stats[0]);
        case VisualMappingOptions.Energy: return HeatColor(primitive.stats[1]);
        default: return neutral;
    }
}

function BudColor(primitive: Primitive) {
    switch (appstate.visualMapping.peek()) {
        case VisualMappingOptions.Natural: return greenColor;
        case VisualMappingOptions.Water: return HeatColor(primitive.stats[0]);
        case VisualMappingOptions.Energy: return HeatColor(primitive.stats[1]);
        default: return neutral;
    }
}

function HeatColor(value: number) {
    const p = Math.max(0, Math.min(1, value)) * heatColorsMax;
    const s = Math.floor(p);
    return s == heatColorsMax ? heatColors[heatColorsMax] : heatColors[s].clone().lerpHSL(heatColors[s + 1], p - s);
}

const neutral = new THREE.Color(neutralColor).lerpHSL(new THREE.Color(backgroundColor), 0.1);
const singleBasicMaterial = new THREE.MeshStandardMaterial({ color: neutral});
export const doubleBasicMaterial = new THREE.MeshStandardMaterial({ color: neutral, side: THREE.DoubleSide});

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