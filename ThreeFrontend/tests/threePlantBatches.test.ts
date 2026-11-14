import * as THREE from 'three';
import { IFlags, InstanceList } from '../src/render/core/InstanceList';
import { ThreePlantBatches, plantHitRef } from '../src/render/three/ThreePlantBatches';
import { applyInstanceMaterial, newInstanceMaterial } from '../src/render/three/ThreeMaterials';

function fixture() {
    const parent = new THREE.Group();
    const geometry = new THREE.BoxGeometry(1, 1, 1);
    const batches = new ThreePlantBatches(parent, () => geometry, inst => {
        const material = newInstanceMaterial();
        applyInstanceMaterial(material, inst.color, inst.emissive, inst.flags, -1);
        return material;
    });
    return { parent, geometry, batches };
}

function add(list: InstanceList, x: number, primitive: number, green = .3, flags = 0) {
    const inst = list.push();
    inst.geom = 0;
    inst.model.set(new THREE.Matrix4().compose(new THREE.Vector3(x, 0, 0),
        new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), .3),
        new THREE.Vector3(1.2, .7, .5)).elements);
    inst.color = { r: .2, g: green, b: .1 };
    inst.emissive = { r: .01, g: .02, b: .03 };
    inst.flags = flags;
    inst.ref = { type: 'plant', index: { entity: primitive % 2, primitive } };
    return inst;
}

function pick(group: THREE.Object3D, x: number) {
    group.updateMatrixWorld(true);
    const ray = new THREE.Raycaster(new THREE.Vector3(x, 0, 5), new THREE.Vector3(0, 0, -1));
    const hits = ray.intersectObjects(group.children, true);
    return { distance: hits[0]?.distance, ref: plantHitRef(hits[0]) };
}

function reference(list: InstanceList, geometry: THREE.BufferGeometry) {
    const group = new THREE.Group();
    for (let i = 0; i < list.count; ++i) {
        const inst = list.items[i];
        if (inst.flags & IFlags.Hidden) continue;
        const material = newInstanceMaterial();
        applyInstanceMaterial(material, inst.color, inst.emissive, inst.flags, -1);
        const mesh = new THREE.Mesh(geometry, material);
        mesh.matrixAutoUpdate = false;
        mesh.matrix.fromArray(inst.model);
        mesh.userData.ref = inst.ref;
        group.add(mesh);
    }
    return group;
}

test('native instance raycasts preserve organ identity and transformed surfaces across growth and reindexing', () => {
    const { parent, geometry, batches } = fixture();
    const list = new InstanceList();
    add(list, -3, 0); add(list, 0, 1); add(list, 3, 2, .7);
    add(list, 6, 3, .3, IFlags.Hidden);
    batches.update(list);
    expect(parent.children[0].children).toHaveLength(2);
    let old = reference(list, geometry);
    for (const x of [-3, 0, 3, 6]) expect(pick(parent, x)).toEqual(pick(old, x));

    // Populate native picking bounds before the frame changes, then grow and move the batch.
    list.reset();
    add(list, 20, 20); add(list, 17, 21); add(list, 14, 22); add(list, 11, 23);
    batches.update(list);
    expect(parent.children[0].children).toHaveLength(1);
    old = reference(list, geometry);
    for (const x of [-3, 0, 3, 11, 14, 17, 20]) expect(pick(parent, x)).toEqual(pick(old, x));

    // A shrinking frame must not retain old instances or stale request identities.
    list.reset(); add(list, -30, 31);
    batches.update(list);
    old = reference(list, geometry);
    for (const x of [-30, 11, 14, 17, 20]) expect(pick(parent, x)).toEqual(pick(old, x));
    batches.dispose();
});

test('keeps exact material values and leaves folded or mirrored organs on the existing mesh path', () => {
    const { parent, geometry, batches } = fixture();
    const list = new InstanceList();
    add(list, 0, 0); add(list, 3, 1, .3 + 1e-12);
    const folded = add(list, 6, 2);
    folded.fold = { angles: [.1, .2, 0, 0], scale: [1, 1, 1] };
    const mirrored = add(list, 9, 3);
    mirrored.model[0] *= -1; mirrored.model[1] *= -1; mirrored.model[2] *= -1;
    batches.update(list);
    const meshes = parent.children[0].children as THREE.InstancedMesh[];
    expect(meshes).toHaveLength(2);
    expect((meshes[0].material as THREE.MeshStandardMaterial).color.g).toBe(.3);
    expect((meshes[1].material as THREE.MeshStandardMaterial).color.g).toBe(.3 + 1e-12);
    expect((meshes[0].material as THREE.MeshStandardMaterial).emissive.toArray()).toEqual([.01, .02, .03]);
    expect(batches.singles.count).toBe(2);
    expect(batches.singles.items[0]).toBe(folded);
    expect(batches.singles.items[1]).toBe(mirrored);
    const disposed = jest.fn(); geometry.addEventListener('dispose', disposed);
    batches.dispose();
    expect(disposed).not.toHaveBeenCalled();
});

test('releases retired instance buffers and materials without disposing shared geometry', () => {
    const { parent, batches } = fixture();
    const list = new InstanceList(); add(list, 0, 0);
    batches.update(list);
    const mesh = parent.children[0].children[0] as THREE.InstancedMesh;
    const bufferDisposed = jest.fn(), materialDisposed = jest.fn();
    mesh.addEventListener('dispose', bufferDisposed);
    (mesh.material as THREE.Material).addEventListener('dispose', materialDisposed);
    list.reset(); batches.update(list);
    expect(bufferDisposed).toHaveBeenCalledTimes(1);
    expect(materialDisposed).toHaveBeenCalledTimes(1);
    expect(parent.children[0].children).toHaveLength(0);
    batches.dispose();
    expect(bufferDisposed).toHaveBeenCalledTimes(1);
});

test('retains sheared normal transforms on the ordinary mesh path', () => {
    const { parent, batches } = fixture();
    const list = new InstanceList();
    add(list, 0, 0);
    const sheared = add(list, 3, 1);
    const model = new THREE.Matrix4().fromArray(sheared.model);
    model.multiply(new THREE.Matrix4().set(1, .4, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1));
    sheared.model.set(model.elements);
    batches.update(list);
    expect(parent.children[0].children).toHaveLength(1);
    expect(batches.singles.count).toBe(1);
    expect(batches.singles.items[0]).toBe(sheared);
    expect((parent.children[0].children[0] as THREE.InstancedMesh).count).toBe(1);
    batches.dispose();
});
