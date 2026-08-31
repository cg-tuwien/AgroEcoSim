//Vanilla orbit camera for the WebGPU backend. No three.
//
//Matches OrbitControls' defaults closely enough that switching backends does not change how
//the viewport feels: left drag orbits, wheel dollies, middle/right drag or shift+drag pans,
//and panning is screen-space (OrbitControls' screenSpacePanning: true).

import { CameraPose } from '../IViewportRenderer';
import { Mat4, createMat4, lookAt, multiply, perspectiveReversedZ } from '../core/Mat4';
import { Vec3, add, sub, length } from '../core/Vec3';

const MIN_POLAR = 1e-6;
const MAX_POLAR = Math.PI - 1e-6;
const ZOOM_SCALE = 0.95;          // OrbitControls' getZoomScale at zoomSpeed 1

export class OrbitCamera {
    target: Vec3 = { x: 0, y: 0, z: 0 };
    up: Vec3 = { x: 0, y: 1, z: 0 };
    fovY = 50;                     // degrees, as three states it
    near = 0.025;
    far = 5000;

    private radius = 5.83;
    private azimuth = 0;
    private polar = Math.PI / 3;

    private viewM = createMat4();
    private projM = createMat4();
    private viewProjM = createMat4();

    constructor(pose?: CameraPose) {
        if (pose) this.setPose(pose);
        else this.setPosition({ x: 0, y: 3, z: 5 });
    }

    get position(): Vec3 {
        const sinPolar = Math.sin(this.polar);
        return add(this.target, {
            x: this.radius * sinPolar * Math.sin(this.azimuth),
            y: this.radius * Math.cos(this.polar),
            z: this.radius * sinPolar * Math.cos(this.azimuth),
        });
    }

    setPosition(p: Vec3) {
        const off = sub(p, this.target);
        this.radius = Math.max(1e-4, length(off));
        this.polar = Math.acos(Math.max(-1, Math.min(1, off.y / this.radius)));
        this.azimuth = Math.atan2(off.x, off.z);
    }

    getPose(): CameraPose {
        return {
            position: this.position,
            target: { ...this.target },
            up: { ...this.up },
            fovY: this.fovY,
            near: this.near,
            far: this.far,
        };
    }

    setPose(pose: CameraPose) {
        this.target = { ...pose.target };
        this.up = { ...pose.up };
        this.fovY = pose.fovY;
        this.near = pose.near;
        this.far = pose.far;
        this.setPosition(pose.position);
    }

    //--- interaction ----------------------------------------------------------------------

    /** Screen-space drag to orbit. Deltas are in pixels. */
    rotate(dx: number, dy: number, viewportHeight: number) {
        //OrbitControls maps a full viewport height of drag to 2*PI of azimuth.
        this.azimuth -= (2 * Math.PI * dx) / Math.max(1, viewportHeight);
        this.polar -= (2 * Math.PI * dy) / Math.max(1, viewportHeight);
        this.polar = Math.max(MIN_POLAR, Math.min(MAX_POLAR, this.polar));
    }

    /** Positive `ticks` dollies out, matching a positive wheel deltaY. */
    dolly(ticks: number) {
        this.radius *= Math.pow(ZOOM_SCALE, -ticks);
        this.radius = Math.max(this.near * 2, Math.min(this.far * 0.5, this.radius));
    }

    /** Screen-space pan: moves the target in the camera's own right/up plane. */
    pan(dx: number, dy: number, viewportHeight: number) {
        //Distance covered by one pixel at the target's depth.
        const halfHeight = Math.tan((this.fovY * Math.PI) / 360) * this.radius;
        const perPixel = (2 * halfHeight) / Math.max(1, viewportHeight);

        const view = this.view();
        //Rows 0 and 1 of the view matrix are the camera's right and up axes in world space.
        const right = { x: view[0], y: view[4], z: view[8] };
        const upAxis = { x: view[1], y: view[5], z: view[9] };

        this.target = add(this.target, {
            x: -right.x * dx * perPixel + upAxis.x * dy * perPixel,
            y: -right.y * dx * perPixel + upAxis.y * dy * perPixel,
            z: -right.z * dx * perPixel + upAxis.z * dy * perPixel,
        });
    }

    //--- matrices -------------------------------------------------------------------------

    view(): Mat4 {
        return lookAt(this.position, this.target, this.up, this.viewM);
    }

    projection(aspect: number): Mat4 {
        //Depth range [0,1]: three's projection is GL-style [-1,1] and reusing it here would
        //clip everything nearer than the midpoint.
        return perspectiveReversedZ((this.fovY * Math.PI) / 180, aspect, this.near, this.far, this.projM);
    }

    viewProjection(aspect: number): Mat4 {
        return multiply(this.projection(aspect), this.view(), this.viewProjM);
    }
}
