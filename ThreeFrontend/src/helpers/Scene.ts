import { Primitive } from "./Primitives";

export type RecordedGeometry = { id: string; profile: number; leaves: number[]; petal: number };
export type PlantModel = { primitives: Primitive[], species: string; geometry?: RecordedGeometry };
export type Index = { entity: number; primitive: number }

