const { chromium } = require('playwright');
const assert = require('node:assert/strict');

(async () => {
    let browser;
    for (let i = 0; i < 30; i++) {
        try { browser = await chromium.connectOverCDP('http://127.0.0.1:9223'); break; }
        catch (error) { if (i === 29) throw error; await new Promise(r => setTimeout(r, 500)); }
    }
    try {
        const page = browser.contexts()[0].pages()[0];
        // Make the native confirmation visibly slow so a single-frame snap-back
        // cannot hide behind a fast local .NET round trip.
        await page.addInitScript(() => {
            Object.defineProperty(window, 'rushHour', {
                configurable: true,
                set(api) {
                    const init = api.init;
                    api.init = reference => init({
                        async invokeMethodAsync(method, ...args) {
                            if (method === 'CommitMove') await new Promise(r => setTimeout(r, 250));
                            return reference.invokeMethodAsync(method, ...args);
                        }
                    });
                    Object.defineProperty(window, 'rushHour', { configurable: true, writable: true, value: api });
                }
            });
        });
        await page.reload();
        await page.locator('#game-board .vehicle:not(:disabled)').first().waitFor();
        await page.locator('select').nth(0).selectOption('6');
        await page.locator('select').nth(1).selectOption('Normal');
        await page.locator('input').fill('20260927');
        await page.getByRole('button', { name: '新しいパズル', exact: true }).click();
        await page.waitForFunction(() => document.body.innerText.includes('6-2-20260927') && !document.querySelector('.vehicle')?.disabled);

        for (const axis of ['h', 'v']) {
            const chosen = await page.evaluate(axis => {
                const size = +document.querySelector('#game-board').style.getPropertyValue('--size');
                const vehicles = [...document.querySelectorAll('.vehicle')].map(e => ({ id: +e.dataset.id, x: +e.dataset.x, y: +e.dataset.y, w: +e.dataset.w, h: +e.dataset.h, axis: e.dataset.axis }));
                for (const v of vehicles.filter(v => v.axis === axis)) {
                    for (const delta of [1, -1]) {
                        const x = v.x + (axis === 'h' ? delta : 0), y = v.y + (axis === 'v' ? delta : 0);
                        if (x < 0 || y < 0 || x + v.w > size || y + v.h > size) continue;
                        if (vehicles.some(o => o.id !== v.id && x < o.x + o.w && x + v.w > o.x && y < o.y + o.h && y + v.h > o.y)) continue;
                        return { ...v, delta, size };
                    }
                }
                return null;
            }, axis);
            assert(chosen, `Movable ${axis} vehicle`);
            const selector = `.vehicle[data-id="${chosen.id}"]`;
            const vehicle = page.locator(selector);
            const box = await vehicle.boundingBox();
            const board = await page.locator('#game-board').boundingBox();
            const cell = board.width / chosen.size;
            const dx = axis === 'h' ? cell * chosen.delta : 0, dy = axis === 'v' ? cell * chosen.delta : 0;
            const x = box.x + box.width / 2, y = box.y + box.height / 2;
            await page.mouse.move(x, y);
            await page.mouse.down();
            await page.mouse.move(x + dx, y + dy, { steps: 10 });
            await page.waitForTimeout(250);
            const hoveredScale = await vehicle.locator('.vehicle-art').evaluate(e => getComputedStyle(e).transform);
            await page.mouse.move(x + dx + (axis === 'v' ? box.width + 30 : 0), y + dy + (axis === 'h' ? box.height + 30 : 0));
            await page.waitForTimeout(250);
            const outsideScale = await vehicle.locator('.vehicle-art').evaluate(e => getComputedStyle(e).transform);
            assert.equal(hoveredScale, outsideScale, 'Dragging scale stays stable when the pointer crosses the vehicle edge');

            await page.evaluate(selector => {
                const element = document.querySelector(selector);
                window.dragFrames = [];
                window.captureDrag = true;
                const frame = () => {
                    if (!window.captureDrag) return;
                    const bounds = element.getBoundingClientRect();
                    window.dragFrames.push({ x: bounds.x, y: bounds.y });
                    requestAnimationFrame(frame);
                };
                requestAnimationFrame(frame);
            }, selector);
            // Cross a one-second Blazor timer update while still holding the car.
            await page.waitForTimeout(1100);
            await page.mouse.up();
            await page.waitForTimeout(700);
            const frames = await page.evaluate(() => { window.captureDrag = false; return window.dragFrames; });
            assert(frames.length >= 10, 'Actual browser frames were captured');
            const worst = Math.max(...frames.map(f => Math.max(Math.abs(f.x - box.x - dx), Math.abs(f.y - box.y - dy))));
            assert(worst < 1, `No old-position or doubled-offset frame: maximum deviation ${worst.toFixed(3)}px`);
            assert.equal(+(await vehicle.getAttribute(axis === 'h' ? 'data-x' : 'data-y')), (axis === 'h' ? chosen.x : chosen.y) + chosen.delta);
            console.log(`PASS: ${axis} drag, ${frames.length} rendered frames, max deviation ${worst.toFixed(3)}px, delayed native confirmation`);
        }
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
