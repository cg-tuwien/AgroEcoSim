//WebGPU device acquisition. Renderer-neutral in the sense that it imports no three.

export type GpuContext = {
    adapter: GPUAdapter;
    device: GPUDevice;
    context: GPUCanvasContext;
    /** The canvas' configured format. Deliberately NON-sRGB - see below. */
    format: GPUTextureFormat;
    canvas: HTMLCanvasElement;
};

/**
 * Requests an adapter, retrying briefly.
 *
 * A single attempt is not reliable: the GPU process may still be starting when the viewport
 * mounts, and `requestAdapter` then resolves to null even though it would succeed a moment
 * later. That is reproducible in headless Chromium and can happen on a real machine after a
 * GPU process restart, so it is worth handling rather than falling back to WebGL for good.
 */
async function requestAdapterWithRetry(attempts = 4, delayMs = 120): Promise<GPUAdapter | null> {
    for (let i = 0; i < attempts; ++i) {
        try {
            const adapter = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' });
            if (adapter) return adapter;
        } catch {
            //Treat a rejection the same as a null result and try again.
        }
        if (i + 1 < attempts) await new Promise(r => setTimeout(r, delayMs));
    }
    return null;
}

/**
 * Raises the buffer limits from the WebGPU defaults to whatever this adapter actually offers.
 *
 * `requestDevice()` with no `requiredLimits` does NOT give you the adapter's capabilities - it
 * gives you the spec's conservative defaults, 128 MiB of storage-buffer binding and 256 MiB per
 * buffer, regardless of what the hardware can do. On the machine this was measured on the
 * adapter offered 1 GiB of each, so asking for nothing threw away eight times the capacity.
 *
 * That is not academic. The instance buffer is 160 bytes per primitive, so the 128 MiB default
 * caps the scene at 838,860 primitives - about 5,100 plants at the ~163 primitives a mature
 * plant carries. The production test site has 7,700. Past the cap Dawn rejects the bind group
 * and every submit that follows, so the viewport fills with garbage rather than reporting
 * anything, and the only sign is an async validation message.
 *
 * Requesting exactly what the adapter reports can never fail - requestDevice only rejects when
 * a required limit EXCEEDS the adapter's - so there is no fallback path to write here. Anything
 * we do not name keeps its default, which is what we want for the rest.
 */
function scaleLimits(adapter: GPUAdapter): Record<string, number> {
    return {
        //Bound by the instance buffer, and by the voxel field buffer at high grid resolutions.
        maxStorageBufferBindingSize: adapter.limits.maxStorageBufferBindingSize,
        //A binding cannot exceed its buffer, so raising one without the other buys nothing.
        maxBufferSize: adapter.limits.maxBufferSize,
    };
}

export async function createGpuContext(
    canvas: HTMLCanvasElement,
    onLost: (info: GPUDeviceLostInfo) => void,
): Promise<GpuContext | null> {
    if (typeof navigator === 'undefined' || !navigator.gpu) {
        console.warn('WebGPU: navigator.gpu is not present');
        return null;
    }

    const adapter = await requestAdapterWithRetry();
    if (!adapter) {
        console.warn('WebGPU: no adapter matched the request');
        return null;
    }

    //Requesting the adapter's own maxima cannot be refused by a spec-conforming implementation,
    //but falling all the way back to WebGL over a limits request would be a poor trade if one
    //ever did. Retry without them and carry on with the defaults.
    let device: GPUDevice;
    try {
        device = await adapter.requestDevice({ requiredLimits: scaleLimits(adapter) });
    } catch (e) {
        console.warn('WebGPU: the adapter refused its own reported limits, falling back to defaults. ' +
                     'Scenes past ~5,000 plants will not fit.', e);
        device = await adapter.requestDevice();
    }

    //`reason === 'destroyed'` is our own teardown and must not trigger a fallback.
    device.lost.then(info => {
        if (info.reason === 'destroyed') return;
        //The message is the only clue a lost device ever gives, and it is not recoverable
        //afterwards. Log it here, where it definitely exists, rather than relying on whoever
        //handles the fallback to pass it on.
        console.error(`WebGPU device lost (${info.reason || 'unknown reason'}): ${info.message || '(no message)'}`);
        onLost(info);
    });

    //WebGPU reports validation failures asynchronously and otherwise silently: a bad draw
    //simply does not appear. Surfacing them is the difference between a five-minute fix and
    //an afternoon of guessing.
    device.addEventListener('uncapturederror', (e: Event) => {
        console.error('WebGPU error:', (e as GPUUncapturedErrorEvent).error.message);
    });

    const context = canvas.getContext('webgpu');
    if (!context) {
        console.warn('WebGPU: canvas.getContext("webgpu") returned null');
        return null;
    }

    //getPreferredCanvasFormat returns the non-sRGB variant, and that is what we want: the
    //fragment shader applies three's LinearTosRGB itself, so the clear colour can be the raw
    //sRGB bytes exactly as WebGLBackground writes them. Configuring an *-srgb format would
    //double-encode.
    const format = navigator.gpu.getPreferredCanvasFormat();

    context.configure({
        device,
        format,
        //Mirrors the WebGL context's { alpha: false }.
        alphaMode: 'opaque',
    });

    return { adapter, device, context, format, canvas };
}
