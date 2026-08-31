//Capability probe for the WebGPU backend.

//A capability probe used to live here. It was removed deliberately: requesting an adapter
//just to throw it away is a second code path that can disagree with the real one - an early
//request can return null simply because the GPU process is still starting - and it made
//'auto' fall back to WebGL on machines where WebGPU works. RendererHost now attempts the
//real mount and falls back only on a real failure.

/** `?gpu=1` forces WebGPU, `?gpu=0` forces Three. Returns undefined when unset. */
export function urlBackendOverride(): 'three' | 'webgpu' | undefined {
    if (typeof location === 'undefined') return undefined;
    const v = new URLSearchParams(location.search).get('gpu');
    if (v === '1') return 'webgpu';
    if (v === '0') return 'three';
    return undefined;
}
