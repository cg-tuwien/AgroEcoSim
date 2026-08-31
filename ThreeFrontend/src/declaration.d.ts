/// <reference types="@webgpu/types" />
// The triple-slash reference is deliberate. Adding "types": ["@webgpu/types"] to tsconfig.json
// would switch off the automatic inclusion of every other @types package, so it is pulled in
// here instead, where the blast radius is zero.

declare module '*.css' {
	const mapping: Record<string, string>;
	export default mapping;
}
