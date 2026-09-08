// enzyme is only needed by component tests. Its dependency chain (cheerio -> undici) does
// not load under this Node/jsdom combination, and it would otherwise abort the entire suite
// during setup - before a single test runs - including tests that never touch enzyme.
// Failing soft here keeps the non-component tests runnable; a component test that actually
// needs enzyme still fails loudly on its own, which is the correct place for that error.
try {
	// eslint-disable-next-line @typescript-eslint/no-var-requires
	const { configure } = require('enzyme');
	// eslint-disable-next-line @typescript-eslint/no-var-requires
	const Adapter = require('enzyme-adapter-preact-pure').default;
	configure({ adapter: new Adapter() });
} catch (e) {
	console.warn('enzyme is unavailable in this environment; component tests will fail:', (e as Error).message);
}
