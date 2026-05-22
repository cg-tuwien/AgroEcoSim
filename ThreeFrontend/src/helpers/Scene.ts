import * as THREE from "three";
import { Primitive } from "./Primitives";

export type PlantModel = { primitives: Primitive[], species: string };
export type Index = { entity: number; primitive: number }

