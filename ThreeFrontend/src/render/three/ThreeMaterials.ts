//Material handling for the Three backend.
//
//Every plant instance now owns exactly one MeshStandardMaterial, which the renderer mutates
//in place. That replaces the old scheme of shared "named" singletons that had to be detected
//by material.name and cloned before mutation - fewer allocations, and no way to accidentally
//recolour every grey primitive at once.

import * as THREE from 'three';
import { RGB } from '../core/Color';
import { IFlags } from '../core/InstanceList';

/**
 * Applies an instance's appearance to a material.
 *
 * `needsUpdate` triggers a shader recompile, so it is set ONLY when a flag that affects the
 * program changes (side, flatShading, vertexColors). Setting it every frame would recompile
 * every material every frame and reduce the viewport to single-digit fps.
 */
export function applyInstanceMaterial(
    material: THREE.MeshStandardMaterial,
    color: RGB,
    emissive: RGB,
    flags: number,
    previousFlags: number,
) {
    material.color.setRGB(color.r, color.g, color.b);
    material.emissive.setRGB(emissive.r, emissive.g, emissive.b);

    const programFlags = IFlags.DoubleSided | IFlags.FlatShaded | IFlags.VertexColors;
    if ((flags & programFlags) !== (previousFlags & programFlags)) {
        material.side = flags & IFlags.DoubleSided ? THREE.DoubleSide : THREE.FrontSide;
        material.flatShading = (flags & IFlags.FlatShaded) !== 0;
        material.vertexColors = (flags & IFlags.VertexColors) !== 0;
        material.needsUpdate = true;
    }

    if (flags & IFlags.DepthBias) {
        material.polygonOffset = true;
        material.polygonOffsetFactor = -2;
    } else if (material.polygonOffset) {
        material.polygonOffset = false;
        material.polygonOffsetFactor = 0;
    }
}

export function newInstanceMaterial(): THREE.MeshStandardMaterial {
    //metalness 0 / roughness 1 are the MeshStandardMaterial defaults and are what make this
    //numerically identical to MeshLambertMaterial under the scene's single hemisphere light.
    return new THREE.MeshStandardMaterial();
}
