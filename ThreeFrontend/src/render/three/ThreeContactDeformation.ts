import * as THREE from 'three';
import { Fold, FOLD_GLSL, deformLeaf } from '../core/ContactDeformation';

type State = { fold?: Fold; angles: { value: THREE.Vector4 }; scale: { value: THREE.Vector3 };
    source?: THREE.BufferGeometry; baked?: THREE.BufferGeometry; signature?: string };
const states = new WeakMap<THREE.Mesh, State>();

/** Patch after other material hooks, so density shading sees the same deformed positions. */
export function bindFold(mesh: THREE.Mesh, fold?: Fold) {
    let state = states.get(mesh);
    if (!state && !fold) return;
    if (!state) {
        state = { angles: { value: new THREE.Vector4() }, scale: { value: new THREE.Vector3() } };
        states.set(mesh, state);
        const material = mesh.material as THREE.Material;
        const previous = material.onBeforeCompile.bind(material), cacheKey = material.customProgramCacheKey.bind(material);
        const uniforms = state;
        material.onBeforeCompile = (shader, renderer) => {
            previous(shader, renderer);
            shader.uniforms.uFold = uniforms.angles; shader.uniforms.uFoldScale = uniforms.scale;
            shader.vertexShader = shader.vertexShader.replace('#include <common>',
                `#include <common>\nuniform vec4 uFold; uniform vec3 uFoldScale;\n${FOLD_GLSL}`)
                .replace('#include <begin_vertex>', 'vec3 transformed = foldLeaf(position, uv.x, uFold, uFoldScale);');
        };
        material.customProgramCacheKey = () => `${cacheKey()}:BroadLeafV1`;
        material.needsUpdate = true;
        const raycast = mesh.raycast.bind(mesh);
        mesh.raycast = (raycaster, hits) => {
            const source = mesh.geometry;
            try { if (states.get(mesh)?.fold) mesh.geometry = bakedGeometry(mesh); raycast(raycaster, hits); }
            finally { mesh.geometry = source; }
        };
    }
    state.fold = fold;
    state.angles.value.fromArray(fold?.angles ?? [0, 0, 0, 0]);
    state.scale.value.fromArray(fold?.scale ?? [0, 0, 0]);
    // A rest-space bounding sphere is not a bound on a folded leaf. GPU culling remains
    // available for rigid organs; picking computes the precise deformed sphere on demand.
    mesh.frustumCulled = !fold;
}

function bakedGeometry(mesh: THREE.Mesh): THREE.BufferGeometry {
    const state = states.get(mesh)!, fold = state.fold!, source = mesh.geometry;
    const signature = `${fold.angles.join(',')}:${fold.scale.join(',')}`;
    if (state.source === source && state.signature === signature && state.baked) return state.baked;
    if (state.source !== source) { state.baked?.dispose(); state.baked = source.clone(); state.source = source; }
    const baked = state.baked!, input = source.getAttribute('position'), uv = source.getAttribute('uv');
    const output = baked.getAttribute('position'), point = { x: 0, y: 0, z: 0 };
    for (let i = 0; i < input.count; ++i) {
        deformLeaf(point, input.getX(i), input.getY(i), input.getZ(i), uv?.getX(i) ?? 0, fold.angles, fold.scale);
        output.setXYZ(i, point.x, point.y, point.z);
    }
    output.needsUpdate = true; baked.computeVertexNormals(); baked.computeBoundingBox(); baked.computeBoundingSphere();
    state.signature = signature;
    return baked;
}

export function disposeFold(mesh: THREE.Mesh) { states.get(mesh)?.baked?.dispose(); states.delete(mesh); }

/** Exporters read CPU vertices and do not execute the vertex shader. */
export function cloneFoldedScene(scene: THREE.Scene): { scene: THREE.Scene; dispose: () => void } {
    const owned: THREE.BufferGeometry[] = [];
    const copy = (source: THREE.Object3D): THREE.Object3D | undefined => {
        // TransformControls owns DOM listeners and cannot be cloned without its
        // camera/canvas constructor arguments. It is an editor overlay, not geometry.
        if ((source as THREE.Object3D & { isTransformControls?: boolean }).isTransformControls) return;
        const target = source.clone(false);
        if (source instanceof THREE.Mesh && target instanceof THREE.Mesh && states.get(source)?.fold) {
            target.geometry = bakedGeometry(source).clone(); owned.push(target.geometry);
        }
        for (const child of source.children) { const cloned = copy(child); if (cloned) target.add(cloned); }
        return target;
    };
    return { scene: copy(scene) as THREE.Scene, dispose: () => owned.forEach(g => g.dispose()) };
}
