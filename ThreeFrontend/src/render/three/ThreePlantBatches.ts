import * as THREE from 'three';
import { IFlags, Instance, InstanceList } from '../core/InstanceList';
import { DataRef } from '../core/Picking';

/** Keep the small-scene mesh path as a visual reference. Detailed trees need fewer calls. */
export const PLANT_BATCH_THRESHOLD = 50000;

type Batch = {
    members: Instance[];
    mesh?: THREE.InstancedMesh;
};

/** Exact geometry/material grouping: no colour quantisation or changes to the draw list. */
function appearanceKey(inst: Instance): string {
    return [inst.geom, inst.flags, inst.color.r, inst.color.g, inst.color.b,
        inst.emissive.r, inst.emissive.g, inst.emissive.b].join(':');
}

function batchableTransform(model: ArrayLike<number>): boolean {
    // InstancedMesh cannot reverse winding or apply a per-instance normal matrix
    // for shear. Keep those transforms on the original Mesh path.
    const m = model;
    const determinant = m[0] * (m[5] * m[10] - m[6] * m[9])
        - m[4] * (m[1] * m[10] - m[2] * m[9])
        + m[8] * (m[1] * m[6] - m[2] * m[5]);
    if (!(determinant > 0) || !Number.isFinite(determinant)) return false;
    const aa = m[0]*m[0] + m[1]*m[1] + m[2]*m[2];
    const bb = m[4]*m[4] + m[5]*m[5] + m[6]*m[6];
    const cc = m[8]*m[8] + m[9]*m[9] + m[10]*m[10];
    const ab = m[0]*m[4] + m[1]*m[5] + m[2]*m[6];
    const ac = m[0]*m[8] + m[1]*m[9] + m[2]*m[10];
    const bc = m[4]*m[8] + m[5]*m[9] + m[6]*m[10];
    // Relative tolerance admits float32 rotation/scale rounding, not visible shear.
    return ab*ab <= 1e-10*aa*bb && ac*ac <= 1e-10*aa*cc && bc*bc <= 1e-10*bb*cc;
}

export function plantHitRef(hit: THREE.Intersection | undefined): DataRef | undefined {
    if (!hit) return undefined;
    const data = hit.object.userData;
    return hit.instanceId === undefined ? data.ref : data.plantMembers?.[hit.instanceId]?.ref;
}

/**
 * Pools a draw per identical appearance, rather than a Mesh and material per organ.
 * Folded organs retain their existing deformation and raycast path in singles.
 * Shared source geometry belongs to ThreeGeometry's cache and is never disposed here.
 */
export class ThreePlantBatches {
    readonly singles = new InstanceList();
    readonly singlesGroup = new THREE.Group();
    private readonly rigidGroup = new THREE.Group();
    private readonly batches = new Map<string, Batch>();
    private readonly geometryFor: (id: number) => THREE.BufferGeometry;
    private readonly materialFor: (inst: Instance) => THREE.Material;

    constructor(
        parent: THREE.Object3D,
        geometryFor: (id: number) => THREE.BufferGeometry,
        materialFor: (inst: Instance) => THREE.Material,
    ) {
        this.geometryFor = geometryFor;
        this.materialFor = materialFor;
        parent.add(this.rigidGroup, this.singlesGroup);
    }

    update(list: InstanceList) {
        this.batches.forEach(batch => { batch.members.length = 0; });
        this.singles.reset();
        for (let i = 0; i < list.count; ++i) {
            const inst = list.items[i];
            // Hidden instances cannot draw or pick, including inside a visible batch group.
            if (inst.flags & IFlags.Hidden) continue;
            if (inst.fold || inst.indexRange || !batchableTransform(inst.model)) {
                this.singles.items[this.singles.count++] = inst;
                continue;
            }
            const key = appearanceKey(inst);
            let batch = this.batches.get(key);
            if (!batch) { batch = { members: [] }; this.batches.set(key, batch); }
            batch.members.push(inst);
        }

        // Create/update live groups before releasing old colours, retaining shared programs.
        this.batches.forEach(batch => {
            const count = batch.members.length;
            if (!count) return;
            const first = batch.members[0];
            let mesh = batch.mesh;
            if (!mesh || mesh.instanceMatrix.count < count) {
                const material = mesh?.material ?? this.materialFor(first);
                if (mesh) { this.rigidGroup.remove(mesh); mesh.dispose(); }
                mesh = new THREE.InstancedMesh(this.geometryFor(first.geom), material,
                    Math.max(count, mesh ? Math.ceil(mesh.instanceMatrix.count * 1.5) : count));
                mesh.matrixAutoUpdate = false;
                // Bounds span a whole geometry/material group. Drawing it is cheap; avoid
                // rebuilding a union sphere over every organ just for camera movement.
                mesh.frustumCulled = false;
                mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
                mesh.userData.plantMembers = batch.members;
                this.rigidGroup.add(mesh);
                batch.mesh = mesh;
            }
            mesh.geometry = this.geometryFor(first.geom);
            const matrices = mesh.instanceMatrix.array as Float32Array;
            for (let i = 0; i < count; ++i) matrices.set(batch.members[i].model, i * 16);
            mesh.count = count;
            mesh.instanceMatrix.needsUpdate = true;
            // Native InstancedMesh picking computes this lazily; a new frame invalidates it.
            mesh.boundingSphere = null;
            mesh.boundingBox = null;
        });

        this.batches.forEach((batch, key) => {
            if (batch.members.length) return;
            if (batch.mesh) this.release(batch.mesh);
            this.batches.delete(key);
        });
    }

    private release(mesh: THREE.InstancedMesh) {
        this.rigidGroup.remove(mesh);
        mesh.dispose();
        const materials = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
        materials.forEach(material => material.dispose());
    }

    dispose() {
        this.batches.forEach(batch => { if (batch.mesh) this.release(batch.mesh); });
        this.batches.clear();
        this.rigidGroup.removeFromParent();
        // Singles and their fold state are disposed by the renderer's ordinary mesh walk.
        this.singles.items.length = 0;
    }
}
