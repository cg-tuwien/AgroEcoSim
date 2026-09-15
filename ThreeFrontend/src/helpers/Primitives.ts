export enum Primitives { Disk, Rectangle, Cylinder, Sphere, Box }

//Keep in sync with OrganTypes in Agro/Organs.cs. The organ code is written as a single
//byte in front of every primitive record and decides the record layout, so any divergence
//desynchronizes the whole binary stream, not just the affected organ.
export enum Organs { Unspecified = 0, Seed = 1, Bud = 2, Root = 3, Stem = 4, Leaf = 5, Petiole = 6, Fruit = 7, Meristem = 8, RizomeMeristem = 9, FlowerBud = 10, FlowerStem = 11, FlowerPadel = 12, FlowerPedicel = 13, FlowerMeristem = 14, FlowerBaseBud = 15 }

type StatsBase = {
    //water, energy, irradiance, dailyResources, dailyProduction, color for leaves
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
    /**
     * Position of this organ within its blade, for a leaf simulated as a chain of segments.
     * Zero for the segment at the sheath, and for any leaf that is not segmented at all.
     *
     * A segment is an ordinary leaf organ on the wire, so without this every one of them would
     * be drawn with the species' whole-leaf outline - and a chain of outlines pinches to a point
     * at every joint instead of running on as one blade.
     */
    laminaSlice: number;
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