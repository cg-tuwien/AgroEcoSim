// Mock Browser API's which are not supported by JSDOM, e.g. ServiceWorker, LocalStorage

// jsdom under jest 27 does not define TextEncoder/TextDecoder. src/helpers/BinaryReader.ts
// constructs a TextDecoder at module scope, so without this any test that touches the scene
// parser fails to import.
import { TextDecoder, TextEncoder } from 'util';

const g = global as any;
if (typeof g.TextDecoder === 'undefined') g.TextDecoder = TextDecoder;
if (typeof g.TextEncoder === 'undefined') g.TextEncoder = TextEncoder;

// src/appstate.ts fetches the species list and opens a SignalR connection at module scope.
// Under jsdom those reach a real socket and reject asynchronously, which crashes the worker
// AFTER the tests have already passed. Stubbing here rather than inside a test file matters:
// ES import hoisting runs a test's imports - and therefore appstate's module body - before
// any assignment in the test body could take effect.
g.fetch = jest.fn(() => Promise.resolve({
	ok: true,
	json: () => Promise.resolve([]),
	text: () => Promise.resolve(''),
	arrayBuffer: () => Promise.resolve(new ArrayBuffer(0)),
}));
if (typeof g.WebSocket === 'undefined')
	g.WebSocket = class { close() { /* noop */ } };

// WebGPU's usage/stage flags are plain global constants, not part of the device object, so
// code that merely *builds* a descriptor references them even when no GPU is involved.
// jsdom defines none of them. The values are from the WebGPU specification.
if (typeof g.GPUBufferUsage === 'undefined')
	g.GPUBufferUsage = {
		MAP_READ: 0x0001, MAP_WRITE: 0x0002, COPY_SRC: 0x0004, COPY_DST: 0x0008,
		INDEX: 0x0010, VERTEX: 0x0020, UNIFORM: 0x0040, STORAGE: 0x0080,
		INDIRECT: 0x0100, QUERY_RESOLVE: 0x0200,
	};
if (typeof g.GPUTextureUsage === 'undefined')
	g.GPUTextureUsage = {
		COPY_SRC: 0x01, COPY_DST: 0x02, TEXTURE_BINDING: 0x04,
		STORAGE_BINDING: 0x08, RENDER_ATTACHMENT: 0x10,
	};
if (typeof g.GPUShaderStage === 'undefined')
	g.GPUShaderStage = { VERTEX: 0x1, FRAGMENT: 0x2, COMPUTE: 0x4 };
if (typeof g.GPUMapMode === 'undefined')
	g.GPUMapMode = { READ: 0x0001, WRITE: 0x0002 };

/**
 * An example how to mock localStorage is given below 👇
 */

/* 
// Mocks localStorage
const localStorageMock = (function() {
	let store = {};

	return {
		getItem: (key) => store[key] || null,
		setItem: (key, value) => store[key] = value.toString(),
		clear: () => store = {}
	};

})();

Object.defineProperty(window, 'localStorage', {
	value: localStorageMock
}); */

//jsdom exposes neither CompressionStream nor DecompressionStream, but the Node runtime
//underneath does. The PNG encoder needs the first for the zlib stream PNG requires, and stored
//simulation frames need both. Stubbing them out would leave the only interesting behaviour of
//either untested - and for the frames, a stub would hide a corrupt round-trip entirely.
{
    const node = require('node:stream/web');
    for (const name of ['CompressionStream', 'DecompressionStream',
                        'ReadableStream', 'WritableStream', 'TransformStream'])
        if (typeof (globalThis as any)[name] === 'undefined' && node?.[name])
            (globalThis as any)[name] = node[name];
}
