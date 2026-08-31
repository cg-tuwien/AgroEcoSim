//Camera paths for offline rendering. Renderer-neutral; no three.

import type { CameraPose } from '../IViewportRenderer';
import { Vec3, sub, add, length } from '../core/Vec3';

export interface ICameraPath {
    poseAt(frame: number, total: number): CameraPose;
}

/**
 * The interactive camera, snapshotted once when the job starts.
 *
 * The pose is copied rather than referenced so the user can keep orbiting during an export
 * without corrupting the output.
 */
export class StaticCamera implements ICameraPath {
    //Explicit fields rather than TypeScript parameter properties: the jest babel config
    //cannot parse `constructor(private x)`, even though webpack can, and these classes are
    //unit-tested.
    private pose: CameraPose;

    constructor(pose: CameraPose) {
        this.pose = pose;
    }

    poseAt(): CameraPose { return this.pose; }
}

/** Orbits the target, keeping the starting radius and elevation. */
export class TurntableCamera implements ICameraPath {
    private base: CameraPose;
    private turns: number;
    private radius: number;
    private elevation: number;
    private startAzimuth: number;

    constructor(base: CameraPose, turns = 1) {
        this.base = base;
        this.turns = turns;
        const off = sub(base.position, base.target);
        this.radius = length(off);
        //Spherical: elevation from the horizontal plane, azimuth around Y.
        this.elevation = Math.asin(this.radius > 0 ? off.y / this.radius : 0);
        this.startAzimuth = Math.atan2(off.z, off.x);
    }

    poseAt(frame: number, total: number): CameraPose {
        const t = total > 1 ? frame / total : 0;
        const azimuth = this.startAzimuth + 2 * Math.PI * this.turns * t;
        const horizontal = this.radius * Math.cos(this.elevation);
        const offset: Vec3 = {
            x: horizontal * Math.cos(azimuth),
            y: this.radius * Math.sin(this.elevation),
            z: horizontal * Math.sin(azimuth),
        };
        return { ...this.base, position: add(this.base.target, offset) };
    }
}
