import * as THREE from "three";

export enum Primitives { Disk, Rectangle, Cylinder, Sphere, Box }

export enum Organs { Root = 1, Bud = 2, Stem = 4, Leaf = 5, Petiole = 6, Fruit = 7, Meristem = 8, FlowerBud = 9, FlowerStem = 10, FlowerPetal = 11, FlowerPetiole = 12, FlowerMeristem = 13, FlowerBaseBud = 14, RizomeMeristem = 15 }

type StatsBase = {
    //water, energy, irradiance, dailyResources, dailyProduction for leaves
    //water, energy, woodRatio for stems
    //water, energy for buds
    //water, energy, woodRatio, dailyResources, dailyProduction for roots
    stats: Float32Array | undefined
}

type PrimitiveBase = StatsBase & {
    affineTransform: Float32Array
    organ: Organs
}

export type Disk = PrimitiveBase & {
    type: Primitives.Disk;
}

export type Rectangle = PrimitiveBase & {
    type: Primitives.Rectangle;
    color: string;
}

export type Cylinder = PrimitiveBase & {
    type: Primitives.Cylinder;
    length: number;
    radius: number;
    color: string;
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