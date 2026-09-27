// Run the desktop with WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9223 first.
const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const assert = require('node:assert/strict');

(async () => {
    fs.mkdirSync('artifacts', { recursive: true });
    let browser;
    for (let attempt = 0; attempt < 30; attempt++) {
        try { browser = await chromium.connectOverCDP('http://127.0.0.1:9223'); break; }
        catch (error) { if (attempt === 29) throw error; await new Promise(resolve => setTimeout(resolve, 500)); }
    }
    const pages = browser.contexts().flatMap(c => c.pages());
    const page = pages.find(p => p.url().includes('0.0.0.0')) || pages[0];
    assert(page, 'WebView2 page must exist');
    const errors = [];
    page.on('pageerror', e => errors.push(String(e)));
    await page.locator('#game-board .vehicle').first().waitFor({ timeout: 45000 });
    await page.waitForFunction(() => !document.querySelector('#game-board .vehicle')?.disabled);
    await page.screenshot({ path: 'artifacts/desktop-6x6.png' });
    const puzzle = await page.evaluate(() => {
        const buttons = [...document.querySelectorAll('#game-board .vehicle')];
        const vehicles = buttons.map(e => ({ Id: +e.dataset.id, X: +e.dataset.x, Y: +e.dataset.y, Width: +e.dataset.w, Height: +e.dataset.h, Axis: e.dataset.axis === 'h' ? 'Horizontal' : 'Vertical', Kind: 'Car', Color: 'red', IsTarget: e.dataset.target === 'true' }));
        return { Size: parseInt(getComputedStyle(document.querySelector('#game-board')).getPropertyValue('--size')), ExitRow: vehicles.find(v => v.IsTarget).Y, Seed: 0, Difficulty: 'Normal', OptimalMoves: 0, Vehicles: vehicles };
    });
    fs.writeFileSync('artifacts/smoke-puzzle.json', JSON.stringify(puzzle));
    const solution = JSON.parse(execFileSync('dotnet', [path.resolve('tests/RushHour.Tests/bin/Release/net8.0/RushHour.Tests.dll'), '--solve', path.resolve('artifacts/smoke-puzzle.json')], { encoding: 'utf8' }));
    assert(solution?.length, 'Solve actual displayed board');
    async function slide(move) {
        const vehicle = puzzle.Vehicles.find(v => v.Id === move.VehicleId);
        const element = page.locator(`.vehicle[data-id="${move.VehicleId}"]`);
        const box = await element.boundingBox();
        const board = await page.locator('#game-board').boundingBox();
        const cell = board.width / puzzle.Size;
        const x = box.x + box.width / 2, y = box.y + box.height / 2;
        await page.mouse.move(x, y);
        await page.mouse.down();
        await page.mouse.move(x + (vehicle.Axis === 'Horizontal' ? move.Delta * cell : 0), y + (vehicle.Axis === 'Vertical' ? move.Delta * cell : 0), { steps: 10 });
        await page.mouse.up();
        await page.waitForTimeout(160);
    }
    await slide(solution[0]);
    await page.getByRole('button', { name: /一手戻す/ }).click();
    await page.waitForTimeout(160);
    const restored = await page.locator(`.vehicle[data-id="${solution[0].VehicleId}"]`).getAttribute(puzzle.Vehicles.find(v => v.Id === solution[0].VehicleId).Axis === 'Horizontal' ? 'data-x' : 'data-y');
    const original = puzzle.Vehicles.find(v => v.Id === solution[0].VehicleId);
    assert.equal(+restored, original.Axis === 'Horizontal' ? original.X : original.Y, 'Undo restores actual rendered position');
    await page.getByRole('button', { name: /ヒント/ }).first().click();
    await page.locator('.vehicle.hint').waitFor();
    for (const move of solution) await slide(move);
    await page.getByText('脱出成功！', { exact: true }).first().waitFor({ timeout: 5000 });
    await page.screenshot({ path: 'artifacts/desktop-win.png' });
    assert.deepEqual(errors, [], 'No JavaScript errors');
    console.log(`PASS: actual WPF/WebView2 UI, drag ${solution.length} solution moves, undo, hint, victory`);
    await browser.close();
})().catch(error => { console.error(error); process.exitCode = 1; });
