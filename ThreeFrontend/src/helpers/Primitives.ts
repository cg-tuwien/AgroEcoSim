import * as THREE from "three";

export enum Primitives { Disk, Rectangle, Cylinder, Sphere, Box }

type StatsBase = {
    //water, energy, irradiance, dailyResources, dailyProduction for leaves
    //water, energy, woodRatio for stems
    //water, energy for buds
    //water, energy, woodRatio, dailyResources, dailyProduction for roots
    stats: Float32Array | undefined
}

type PrimitiveBase = StatsBase & {
    affineTransform: Float32Array
}

export type Disk = PrimitiveBase & {
    type: Primitives.Disk;
}

export type Rectangle = PrimitiveBase & {
    type: Primitives.Rectangle;
}

export type Cylinder = PrimitiveBase & {
    type: Primitives.Cylinder;
    length: number;
    radius: number;
}

export type Sphere = StatsBase & {
    type: Primitives.Sphere;
    radius: number;
    center: Float32Array;
}

export type Box = PrimitiveBase & {
    type: Primitives.Box;
    length: number;
    radius: number;
}

export type Primitive = Disk | Rectangle | Cylinder | Sphere | Box;