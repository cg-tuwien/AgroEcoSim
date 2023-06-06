import * as THREE from "three";

type PrimitiveBase = {
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

export type Sphere = {
    type: 4;
    radius: number;
    center: Float32Array;
}

export type Primitive = Disk | Rectangle | Cylinder | Sphere;