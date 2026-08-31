import { Signal, batch, signal } from "@preact/signals";
import appstate from "../appstate";
import { BaseRequestObject } from "./BaseRequestObject";
import { MeshData, computeVertexNormals } from "../render/core/Geometry";
import { makeSelectionPalette } from "../render/core/SelectionPalette";
import { Geom } from "../render/core/GeometryIds";
import { Mat4, identity, scaleInPlace, setPosition } from "../render/core/Mat4";

const obstaclePalette = makeSelectionPalette("#999");

//Stable identity for geometry registration. Obstacles have no natural key (imported meshes
//share the type "mesh" and carry no id), and keying on list position would re-register a
//different geometry whenever the list is reordered.
let nextObstacleUid = 0;

export type ObstacleType = "wall" | "umbrella" | "mesh";

export class Obstacle extends BaseRequestObject
{
    type: Signal<ObstacleType>;
    angleX: Signal<number>;
    angleY: Signal<number>;
    wallLength_UmbrellaRadius: Signal<number>;
    height: Signal<number>;
    thickness: Signal<number>;

    vertices: Float32Array;
    faces: number[];
    /** Renderer-neutral form of an imported mesh obstacle; undefined for wall/umbrella. */
    meshData: MeshData | undefined;
    /** Runtime geometry id assigned by the renderer when it registers meshData. */
    meshGeom: number | undefined;
    readonly uid = nextObstacleUid++;

    constructor(type: ObstacleType, x: number, y: number, z: number, ax: number, ay: number, l: number, h: number, t: number, verts: Float32Array, faces: number[]) {
        super(x, y, z, obstaclePalette);

        this.type = signal(type);
        this.angleX = signal(ax);
        this.angleY = signal(ay);
        this.wallLength_UmbrellaRadius = signal(l);
        this.height= signal(h);
        this.thickness = signal(t);

        this.vertices = verts;
        this.faces = faces;

        if (type === "mesh")
        {
            this.movable = false;
            //Built as neutral MeshData so either renderer can consume the same arrays.
            const indices = Uint32Array.from(this.faces);
            this.meshData = {
                positions: this.vertices,
                normals: computeVertexNormals(this.vertices, indices),
                indices,
                name: 'obstacle:mesh',
            };
        }

        appstate.needsRender.value = true;
    }

    /**
     * Which geometry to draw. Mesh obstacles resolve to the runtime id the renderer assigned
     * when it registered meshData; the renderer falls back to a plain box if it has not.
     */
    geomId(): number {
        switch (this.type.value)
        {
            case "wall": return Geom.BoxBottomCentered;
            case "umbrella": return Geom.CircleUnit;
            default: return this.meshGeom ?? Geom.BoxBottomCentered;
        }
    }

    /**
     * Writes this obstacle's model matrix.
     *
     * Replaces setupWall/setupUmbrella plus the two effects that used to push scale and
     * position into mesh. Reads through `.value` so a renderer effect re-runs on change.
     * Umbrellas are anchored at their post, hence the +height on Y, and use half the
     * diameter as the radius scale.
     */
    writeModel(out: Mat4): Mat4 {
        identity(out);
        const type = this.type.value;
        const lr = this.wallLength_UmbrellaRadius.value;
        const h = this.height.value;
        const t = this.thickness.value;

        switch (type)
        {
            case "wall":
                scaleInPlace(out, lr, h, t);
                return setPosition(out, this.px.value, this.py.value, this.pz.value);
            case "umbrella":
                scaleInPlace(out, lr * 0.5, h, lr * 0.5);
                return setPosition(out, this.px.value, this.py.value + h, this.pz.value);
            default:
                return setPosition(out, this.px.value, this.py.value, this.pz.value);
        }
    }

    static rndObstacle() {
        const type = Math.random() > 0.5 ? "wall" : "umbrella";
        const isWall = type == "wall";
        return new Obstacle(type,
            Math.random() * appstate.fieldSizeX.value, 0, Math.random() * appstate.fieldSizeZ.value,
            0, 0,
            isWall ? 4 : 1, isWall ? 3 : 2.2,  isWall ? 0.4 : 0.08, undefined, undefined);
    }

    /** Undoes the umbrella's drawing offset so px/py/pz stay in anchor space. */
    transformMove(x: number, y: number, z: number) {
        if (this.movable)
            batch(() => {
                this.px.value = x;
                this.py.value = y - (this.type.peek() == "wall" ? 0 : this.height.peek());
                this.pz.value = z;
                appstate.needsRender.value = true;
            });
    }

    public save() {
        return {
            px: this.px.peek(),
            py: this.py.peek(),
            pz: this.pz.peek(),
            type: this.type.peek(),
            l: this.wallLength_UmbrellaRadius.peek(),
            h: this.height.peek(),
            t: this.thickness.peek(),
            ax: this.angleX.peek(),
            ay: this.angleY.peek(),
            //vertices/faces are only populated for imported mesh obstacles.
            vt: this.vertices ? Array.from(this.vertices) : [],
            fc: this.faces ?? []
        }
    }
}
