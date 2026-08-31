//RGB <-> THREE.Color. Part of the Three adapter, so importing three here is expected.

import * as THREE from 'three';
import { RGB } from '../core/Color';

/**
 * Linear RGB -> THREE.Color.
 *
 * setRGB defaults its colourSpace argument to the working space (Linear-sRGB in r152), so
 * toWorkingColorSpace is a no-op and the linear values pass through untouched. Do NOT switch
 * this to `new THREE.Color(hex)` - that would apply an sRGB decode and darken everything.
 */
export function toThreeColor(c: RGB): THREE.Color {
    return new THREE.Color().setRGB(c.r, c.g, c.b);
}

/** Writes into an existing THREE.Color, avoiding an allocation in the per-primitive paths. */
export function setThreeColor(target: THREE.Color, c: RGB): THREE.Color {
    return target.setRGB(c.r, c.g, c.b);
}

export function fromThreeColor(c: THREE.Color): RGB {
    return { r: c.r, g: c.g, b: c.b };
}
