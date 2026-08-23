// Does the deployed page actually make a noise?
//
//     node tools/web-port/browser_check.mjs [url]
//
// **The gap this closes.** Every other check here runs the GDScript tree headless under
// the desktop engine, and the desktop engine is exactly where the audio bug could not
// happen: Godot ships `audio/general/default_playback_type.web` as Sample and everything
// else as Stream, so the broken path only ever ran in a browser. Sixty passing checks and
// a silent game, three deploys running.
//
// So this one drives the real page in a real browser and measures the waveform. It taps
// every node that connects to the AudioContext destination with an AnalyserNode, presses
// the key that throws every die, and reads the RMS. Silence is a failure.
//
// Needs Playwright and a Chrome install:
//     npm i playwright && npx playwright install chrome     (or use the system Chrome)

import {chromium} from 'playwright';

const URL = process.argv[2] || process.env.GAME_URL
	|| 'https://dimitriuses.github.io/godot-first-game/';

let failures = 0;
function check(what, ok, detail) {
	console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${what.padEnd(44)}${detail}`);
	if (!ok) { failures++; }
}

console.log(`browser check: ${URL}\n`);

// A local machine usually has Chrome; CI has Playwright's own chromium. Either will do —
// what is being measured is Godot's audio graph, not the browser's chrome.
const browser = await chromium.launch({channel: 'chrome'})
	.catch(() => chromium.launch());
const page = await browser.newPage();
page.setDefaultTimeout(60000);

const logs = [];
page.on('console', (m) => logs.push(m.text()));
page.on('pageerror', (e) => logs.push('PAGEERROR: ' + e.message));

// Splice an analyser onto everything that reaches the speakers, and count the node kinds
// Godot builds — which is how the playback path is identified from outside.
await page.addInitScript(() => {
	const P = window.__probe = {analyser: null, ctx: null, kinds: {}};
	const orig = AudioNode.prototype.connect;
	AudioNode.prototype.connect = function (dest, ...rest) {
		try {
			const kind = this.constructor && this.constructor.name;
			P.kinds[kind] = (P.kinds[kind] || 0) + 1;
			if (dest && dest.context && dest === dest.context.destination) {
				P.ctx = dest.context;
				if (!P.analyser) {
					P.analyser = dest.context.createAnalyser();
					P.analyser.fftSize = 2048;
				}
				orig.call(this, P.analyser);
			}
		} catch (e) { /* never break the page */ }
		return orig.call(this, dest, ...rest);
	};
});

await page.goto(URL, {waitUntil: 'domcontentloaded', timeout: 120000});
const started = await page.waitForFunction(
	() => window.__probe && window.__probe.ctx, {timeout: 150000})
	.then(() => true).catch(() => false);
check('the engine starts and opens an AudioContext', started,
	started ? 'destination connected' : 'no AudioContext after 150s');
if (!started) { await finish(); }

await page.waitForTimeout(5000);
await page.locator('canvas').click({position: {x: 600, y: 400}});
await page.waitForTimeout(600);

const state = await page.evaluate(() => {
	const w = window.__diceWeb || {};
	const ctx = window.__probe.ctx;
	return {glue: w.state, flag: w.audio, ctxState: ctx.state};
});
check('the AudioContext is running', state.ctxState === 'running',
	`state '${state.ctxState}', glue reports '${state.glue}'`);

async function measure(ms) {
	return page.evaluate(async (duration) => {
		const P = window.__probe;
		const buf = new Float32Array(P.analyser.fftSize);
		let peak = 0, sum = 0, n = 0;
		const until = performance.now() + duration;
		while (performance.now() < until) {
			P.analyser.getFloatTimeDomainData(buf);
			let s = 0;
			for (let i = 0; i < buf.length; i++) {
				s += buf[i] * buf[i];
				if (Math.abs(buf[i]) > peak) { peak = Math.abs(buf[i]); }
			}
			sum += Math.sqrt(s / buf.length);
			n++;
			await new Promise((r) => setTimeout(r, 16));
		}
		return {rms: sum / Math.max(1, n), peak};
	}, ms);
}

const quiet = await measure(600);
check('a settled board is quiet', quiet.peak < 0.001,
	`peak ${quiet.peak.toFixed(5)}`);

// Space throws every die: a clatter, then a dozen landings. The loudest thing it does.
await page.keyboard.press('Space');
const loud = await measure(2500);
check('throwing every die makes a noise', loud.peak > 0.001,
	`peak ${loud.peak.toFixed(5)}, rms ${loud.rms.toFixed(6)}`);

const kinds = await page.evaluate(() => window.__probe.kinds);
// Not a failure on its own — it is the fingerprint that says which path is in use, and
// the reason to look here first if the sound goes away again.
console.log(`\n  playback path: ${
	kinds.AudioBufferSourceNode ? 'Sample (AudioBufferSourceNode present)'
		: 'Stream (engine mixer only)'}`);
console.log('  nodes built:  ' + JSON.stringify(kinds));

async function finish() {
	console.log('\n  page console:');
	for (const l of logs) { console.log('     ' + l); }
	console.log(failures === 0 ? '\nall checks passed' : `\n${failures} FAILED`);
	await browser.close();
	process.exit(failures === 0 ? 0 : 1);
}
await finish();
