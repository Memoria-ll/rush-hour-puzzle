const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const handlers = new Map();
const commits = [];
const classes = new Set();
let capture = null;
const button = {
    dataset: { id: '0' }, style: {},
    classList: { add: c => classes.add(c), remove: c => classes.delete(c), toggle() {} },
    focus() {}, setPointerCapture: id => capture = id,
    hasPointerCapture: id => capture === id,
    releasePointerCapture: () => capture = null,
    closest: () => button
};
const board = { contains: () => true, getBoundingClientRect: () => ({ width: 600 }) };
const context = { console, document: {
    getElementById: () => board, querySelector: () => null, querySelectorAll: () => [button],
    addEventListener: (n, h) => handlers.set(n, h), removeEventListener: n => handlers.delete(n)
}, window: { addEventListener: (n, h) => handlers.set(n, h), removeEventListener: n => handlers.delete(n) } };
vm.runInNewContext(fs.readFileSync('src/RushHour.Desktop/wwwroot/drag.js', 'utf8'), context);
const api = context.window.rushHour;
let reply = async () => false;
api.init({ invokeMethodAsync: async (...args) => { commits.push(args); return reply(); } });
const car = { id: 0, x: 0, y: 2, width: 2, height: 1, horizontal: true, isTarget: true };
const blocker = { id: 1, x: 3, y: 1, width: 1, height: 2, horizontal: false, isTarget: false };
const event = (x, overrides = {}) => ({ target: button, button: 0, pointerId: 1, clientX: x, clientY: 250, preventDefault() {}, ...overrides });
(async () => {
    api.sync(6, [car, blocker], false, 1);
    handlers.get('pointerdown')(event(100));
    handlers.get('pointermove')(event(800));
    assert.equal(button.style.transform, 'translate3d(100px,0px,0)');
    handlers.get('pointerup')(event(800));
    await new Promise(setImmediate);
    assert.deepEqual(commits.pop(), ['CommitMove', 0, 1, 1]);
    assert.equal(capture, null);
    assert.equal(button.style.transform, '');
    api.sync(6, [car], false, 2);
    handlers.get('pointerdown')(event(100));
    handlers.get('pointerup')(event(650));
    await new Promise(setImmediate);
    assert.deepEqual(commits.pop(), ['CommitMove', 0, 5, 2]);
    handlers.get('pointerdown')(event(100));
    handlers.get('pointermove')(event(300));
    handlers.get('pointercancel')(event(300));
    assert.equal(commits.length, 0);
    assert.equal(button.style.transform, '');
    handlers.get('keydown')(event(0, { key: 'ArrowRight', shiftKey: true }));
    await new Promise(setImmediate);
    assert.deepEqual(commits.pop(), ['CommitMove', 0, 5, 2]);
    api.sync(6, [car], true, 3);
    handlers.get('pointerdown')(event(100));
    assert.equal(capture, null);

    // Delay .NET deliberately: no frame may show the original cell on release.
    let complete;
    reply = () => new Promise(resolve => { complete = resolve; });
    api.sync(6, [car], false, 4);
    handlers.get('pointerdown')(event(100));
    handlers.get('pointerup')(event(200));
    assert.equal(button.style.left, '16.6667%', 'Drop is visible before .NET replies');
    assert.equal(button.style.transform, '');
    api.sync(6, [car], false, 4);
    assert.equal(button.style.left, '16.6667%', 'An old snapshot cannot undo the preview');
    complete(true);
    await new Promise(setImmediate);
    handlers.get('pointerdown')(event(200));
    assert.equal(capture, null, 'Await the confirmed board, not just the RPC response');
    api.sync(6, [{ ...car, x: 1 }], false, 5);
    assert.equal(button.style.left, '16.6667%', 'Confirmation preserves the visible position');

    // Rejected moves restore the last authoritative position.
    handlers.get('pointerdown')(event(200));
    handlers.get('pointerup')(event(300));
    assert.equal(button.style.left, '33.3333%');
    complete(false);
    await new Promise(setImmediate);
    assert.equal(button.style.left, '16.6667%', 'Rejected move rolls back');

    // A late rejection from the previous board must not overwrite a new game.
    handlers.get('pointerdown')(event(200));
    handlers.get('pointerup')(event(300));
    const rejectPrevious = complete;
    api.sync(6, [{ ...car, x: 3 }], false, 6);
    rejectPrevious(false);
    await new Promise(setImmediate);
    assert.equal(button.style.left, '50.0000%', 'New board survives a late old response');

    // Render can also arrive before the RPC response; a later move stays locked.
    handlers.get('pointerdown')(event(400));
    handlers.get('pointerup')(event(300));
    const acceptPrevious = complete;
    api.sync(6, [{ ...car, x: 2 }], false, 7);
    handlers.get('pointerdown')(event(300));
    handlers.get('pointerup')(event(200));
    acceptPrevious(true);
    await new Promise(setImmediate);
    handlers.get('pointerdown')(event(200));
    assert.equal(capture, null, 'An old response cannot unlock a newer move');
    complete(false);
    await new Promise(setImmediate);
    assert.equal(button.style.left, '33.3333%');
    api.dispose();
    assert.equal(handlers.size, 0);
    console.log('PASS: drag rules, no snap-back, rejection rollback, both render/RPC orderings, disposal');
})().catch(e => { console.error(e); process.exitCode = 1; });
