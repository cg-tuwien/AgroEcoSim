//The sky background for the Three/WebGL backend.
//
//A CubeTexture assigned to scene.background, filled from the six CPU-built faces in
//core/SkyTexture. Three draws it with its own built-in background shader, so there is no GLSL
//here at all - which is the point: the WebGPU backend samples the SAME bytes, so the two
//cannot drift.
//
//Why a cube and not an equirect DataTexture, since the equirect route looks simpler: it does
//work - WebGLBackground.render passes the background through cubemaps.get(), and
//WebGLCubeMaps converts an EquirectangularReflectionMapping texture into a cube render target,
//so it reaches the same directional shader. But that conversion is cached in a WeakMap keyed on
//the texture INSTANCE with no version check, so a face buffer refilled in place would render
//the first sky forever; escaping it means disposing and reallocating the DataTexture every
//timestep, and each conversion costs a six-face GPU render pass. A CubeTexture is uploaded
//directly and updates in place.

import * as THREE from 'three';
import type { SkyCube } from '../core/SkyTexture';

export class ThreeSky {
    private texture: THREE.CubeTexture | undefined;
    private faces: THREE.DataTexture[] = [];
    private uploaded = -1;

    /**
     * @returns what scene.background should be set to, or undefined to fall back to the flat
     *   theme colour.
     */
    update(cube: SkyCube | undefined): THREE.CubeTexture | undefined {
        if (!cube) return undefined;

        if (!this.texture || this.faces.length === 0 || this.faces[0].image.width !== cube.size)
            this.allocate(cube);

        //`revision` as a number, not the cube's identity: resolveSky hands back the same object
        //with its buffers rewritten, so comparing references would upload once and go stale.
        if (this.uploaded !== cube.revision) {
            //The face buffers are the same Uint8Arrays the DataTextures already point at, so
            //there is nothing to copy - only a version bump, which is what makes three reupload
            //through texSubImage2D rather than reallocating.
            for (const face of this.faces) face.needsUpdate = true;
            this.texture!.needsUpdate = true;
            this.uploaded = cube.revision;
        }
        return this.texture;
    }

    private allocate(cube: SkyCube) {
        this.dispose();
        //Six DataTextures, not six bare {data,width,height} objects: WebGLTextures picks the
        //upload path with `texture.image[0].isDataTexture`, and a plain object goes down a
        //DOM-flavoured texImage2D that will not accept it.
        this.faces = cube.faces.map(data => {
            const t = new THREE.DataTexture(data, cube.size, cube.size);
            t.needsUpdate = true;
            return t;
        });
        this.texture = new THREE.CubeTexture(this.faces);
        //The faces hold already-exposed sRGB bytes. SRGBColorSpace makes WebGL2 allocate
        //SRGB8_ALPHA8, so the sampler decodes to linear and therefore FILTERS in linear space -
        //the WebGPU side uses an -srgb format for the same reason. It also makes
        //WebGLBackground set toneMapped = false, leaving encodings_fragment to re-encode on
        //output, so the bytes round-trip.
        this.texture.colorSpace = THREE.SRGBColorSpace;
        this.texture.format = THREE.RGBAFormat;
        this.texture.type = THREE.UnsignedByteType;
        this.texture.magFilter = THREE.LinearFilter;
        //Must be a non-mipmap filter: there are no mipmaps and asking for them would make every
        //sample fall back to the base level anyway, after paying to generate them.
        this.texture.minFilter = THREE.LinearFilter;
        this.texture.generateMipmaps = false;
        this.texture.needsUpdate = true;
        this.uploaded = -1;
    }

    dispose() {
        for (const face of this.faces) face.dispose();
        this.faces = [];
        this.texture?.dispose();
        this.texture = undefined;
        this.uploaded = -1;
    }
}
