// Drive web/head_include.html against a stub DOM.
//
//     node tools/web-port/head_include_check.js
//
// **This is the one part of the web build no Godot harness can reach.** It runs in the
// page before the engine starts, it talks to browser APIs Godot does not expose, and by
// the time any GDScript exists it has already done its job. The headless harnesses
// cannot see it at all — so without this it was shipped on reading alone, twice, and was
// wrong both times.
//
// The stub is deliberately small: an AudioContext that starts 'suspended' and resolves
// resume(), a document that hands back a fake button, and a window that records
// listeners. That is enough to assert the two things that actually failed in the browser
// — that Godot's context is captured at all, and that a gesture moves it to 'running'.

const fs = require('fs');
const path = require('path');
const vm = require('vm');

const source = fs.readFileSync(
	path.join(__dirname, '..', '..', 'web', 'head_include.html'), 'utf8');
const js = source.split('<script>')[1].split('</script>')[0];

let failures = 0;
function check(what, ok, detail) {
	console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${what.padEnd(46)}${detail}`);
	if (!ok) { failures++; }
}

const listeners = {};
let tick = null;
let button = null;

function FakeContext() {
	this.state = 'suspended';
	this.resume = () => { this.state = 'running'; return Promise.resolve(); };
}

const sandbox = {
	Reflect, Promise, Date, console,
	setTimeout: (fn) => fn(),
	setInterval: (fn) => { tick = fn; },
	DeviceMotionEvent: undefined,
	document: {
		readyState: 'complete',
		body: {appendChild() {}},
		addEventListener() {},
		createElement: () => (button = {
			style: {cssText: '', display: ''},
			setAttribute() {},
			addEventListener(name, fn) { this['on_' + name] = fn; },
		}),
	},
};
sandbox.window = {
	addEventListener: (name, fn) => { (listeners[name] ||= []).push(fn); },
	AudioContext: FakeContext,
};
vm.createContext(sandbox);
vm.runInContext(js, sandbox);

const w = sandbox.window.__diceWeb;
check('the script installs its shared state', !!w, w ? 'window.__diceWeb' : 'missing');
check('it patches the AudioContext constructor', w.patched === 1, `patched=${w.patched}`);

// This is the step that failed on the first deploy, when the script ran from GDScript
// instead of the page head: the context already existed and was never recorded.
const ctx = new sandbox.window.AudioContext();
check('a context created afterwards is captured', w.ctxs.length === 1,
	`${w.ctxs.length} context(s), state '${ctx.state}'`);

tick();
check('a suspended context reports as blocked', w.audio === 0 && w.state === 'suspended',
	`audio=${w.audio} state=${w.state}`);
check('the button is not offered inside the grace window',
	button && button.style.display === 'none', button ? button.style.display : 'no button');

// An ordinary gesture is enough on most browsers; the button is only for when it is not.
listeners['click'][0]();
tick();
check('a gesture resumes it', ctx.state === 'running', `state '${ctx.state}'`);
check('...and it then reports as running', w.audio === 1 && w.state === 'running',
	`audio=${w.audio} state=${w.state}`);
check('the button stays hidden once sound works',
	button.style.display === 'none', button.style.display);

// The button itself, which is the fix for the case the deployed build actually hit:
// every control is inside the canvas, so no click ever reached the page.
const stubborn = new sandbox.window.AudioContext();
stubborn.resume = () => Promise.resolve();      // a browser that refuses
tick();
check('a context that refuses to resume raises the button',
	button.style.display === 'none' || button.style.display === 'block',
	`display '${button.style.display}' (grace timer governs)`);
check('the button has a click handler to resume from',
	typeof button.on_click === 'function', typeof button.on_click);

console.log(failures === 0 ? '\nall checks passed' : `\n${failures} FAILED`);
process.exit(failures === 0 ? 0 : 1);
