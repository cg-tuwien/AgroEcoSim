import * as THREE from "three";

type StatsBase = {
    //water, energy, irradiance, dailyResources, dailyProduction for leaves
    //water, energy, woodRatio for stems
    //water, energy for buds
    stats: Float32Array | undefined

}

type PrimitiveBase = StatsBase & {
    affineTransform: Float32Array
}

export type Disk = PrimitiveBase & {
    type: 1;
}

export type Rectangle = PrimitiveBase & {
    type: 8;
}

export type Cylinder = PrimitiveBase & {
    type: 2;
    length: number;
    radius: number;
}

export type Sphere = StatsBase & {
    type: 4;
    radius: number;
    center: Float32Array;
}

export type Primitive = Disk | Rectangle | Cylinder | Sphere;