const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');

// Serve only published files, with no SPA fallback or development-server behavior.
const root = path.resolve('dist/web/wwwroot');
const prefix = '/rush-hour-puzzle/';
const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm', '.dat': 'application/octet-stream' };
const server = http.createServer((req, res) => {
    const url = new URL(req.url, 'http://localhost');
    if (!url.pathname.startsWith(prefix)) { res.writeHead(404).end(); return; }
    const file = path.resolve(root, decodeURIComponent(url.pathname.slice(prefix.length)) || 'index.html');
    if (!file.startsWith(root + path.sep)) { res.writeHead(403).end(); return; }
    fs.readFile(file, (error, data) => {
        if (error) { res.writeHead(404).end(); return; }
        res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' });
        res.end(data);
    });
});

(async () => {
    assert(fs.existsSync(path.join(root, '_framework/blazor.webassembly.js')), 'Publish the web project first');
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const browser = await chromium.launch({ headless: true });
    const errors = [];
    try {
        const page = await browser.newPage({ viewport: { width: 1180, height: 820 } });
        page.setDefaultTimeout(120000);
        page.on('pageerror', error => errors.push(error.message));
        page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
        page.on('response', response => { if (response.status() >= 400) errors.push(`${response.status()} ${response.url()}`); });
        await page.goto(`http://127.0.0.1:${server.address().port}${prefix}`);
        const ready = () => page.waitForFunction(() => document.querySelector('.vehicle') && !document.querySelector('.vehicle').disabled);
        await ready();
        await page.getByText('BROWSER', { exact: true }).waitFor();
        await page.locator('input').fill('91721');
        await page.getByRole('button', { name: '新しいパズル', exact: true }).click();
        await ready();
        const positions = () => page.locator('.vehicle').evaluateAll(elements => elements.map(e => [e.dataset.id, e.dataset.x, e.dataset.y]));
        const initial = await positions();
        await page.getByRole('button', { name: '新しいパズル', exact: true }).click();
        await ready();
        assert.deepEqual(await positions(), initial, 'Same seed reproduces board');

        // Follow the real hint service and drag actual DOM vehicles all the way to victory.
        const moves = page.locator('.score-v').first();
        async function hintMove() {
            await page.getByRole('button', { name: 'ヒント', exact: true }).click();
            await page.locator('.vehicle.hint:not([disabled])').waitFor();
            const text = await page.locator('.note-hint').textContent();
            const [, direction, count] = text.match(/を(右|左|下|上)へ(\d+)マス/);
            const vehicle = page.locator('.vehicle.hint');
            const box = await vehicle.boundingBox();
            const board = await page.locator('#game-board').boundingBox();
            const size = await page.locator('#game-board').evaluate(e => +e.style.getPropertyValue('--size'));
            const step = board.width / size * Number(count);
            const x = box.x + box.width / 2, y = box.y + box.height / 2;
            const before = +(await moves.textContent());
            await page.mouse.move(x, y);
            await page.mouse.down();
            await page.mouse.move(x + (direction === '右' ? step : direction === '左' ? -step : 0), y + (direction === '下' ? step : direction === '上' ? -step : 0), { steps: 8 });
            await page.mouse.up();
            await page.waitForFunction(n => Number(document.querySelector('.score-v').textContent) === n, before + 1);
        }
        await hintMove();
        await page.getByRole('button', { name: '一手戻す', exact: true }).click();
        await page.waitForFunction(() => document.querySelector('.score-v').textContent === '0');
        assert.deepEqual(await positions(), initial, 'Undo restores the board');
        let count = 0;
        while (!(await page.locator('.overlay-win').count()) && count++ < 25) await hintMove();
        assert(await page.locator('.overlay-win').count(), 'Hint path wins');
        fs.mkdirSync('artifacts', { recursive: true });
        await page.screenshot({ path: 'artifacts/web-win.png' });
        await page.getByRole('button', { name: '同じ盤面をもう一度', exact: true }).click();
        await ready();
        assert.deepEqual(await positions(), initial, 'Restart restores the board');

        // The large-board fallback uses embedded JSON and must survive Release trimming.
        await page.locator('select').nth(0).selectOption('12');
        await page.locator('select').nth(1).selectOption('Expert');
        await page.locator('input').fill('20260927');
        await page.getByRole('button', { name: '新しいパズル', exact: true }).click();
        await ready();
        assert.equal(await page.locator('#game-board').evaluate(e => e.style.getPropertyValue('--size')), '12');
        await page.getByRole('button', { name: 'ヒント', exact: true }).click();
        await page.locator('.vehicle.hint:not([disabled])').waitFor();
        assert(await page.locator('.vehicle[data-w="2"][data-h="2"]').count(), 'Large vehicles render');

        for (const width of [390, 320]) {
            await page.setViewportSize({ width, height: 844 });
            await page.locator('#game-board').scrollIntoViewIfNeeded();
            assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Mobile page has no horizontal overflow');
            const board = await page.locator('#game-board').boundingBox();
            assert(board.width > 220 && board.x >= 0 && board.x + board.width <= width, 'Mobile board fits');
            await page.screenshot({ path: `artifacts/web-mobile-${width}.png`, fullPage: true });
        }
        await page.getByRole('button', { name: '遊び方', exact: true }).click();
        await page.getByRole('dialog').waitFor();
        await page.keyboard.press('Escape');
        await page.getByRole('dialog').waitFor({ state: 'hidden' });
        assert.deepEqual(errors, [], 'No browser or HTTP errors');
        console.log('PASS: published app at repository subpath; seed, drag, undo, hints, victory, restart, 12x12 expert, mobile layout, help');
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; }).finally(() => server.close());
