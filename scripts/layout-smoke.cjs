const { chromium } = require('playwright');
const assert = require('node:assert/strict');
(async () => {
    const browser = await chromium.connectOverCDP('http://127.0.0.1:9223');
    const page = browser.contexts()[0].pages()[0];
    const replay = page.getByRole('button', { name: '同じ盤面をもう一度', exact: true });
    if (await replay.count()) await replay.click();
    for (const size of [8, 12]) {
        await page.locator('select').nth(0).selectOption(String(size));
        await page.locator('select').nth(1).selectOption('Expert');
        await page.locator('input').fill('20260927');
        await page.getByRole('button', { name: '新しいパズル', exact: true }).click();
        await page.waitForFunction(n => document.querySelector('#game-board')?.style.getPropertyValue('--size').trim() === String(n) && !document.querySelector('.vehicle')?.disabled, size, { timeout: 30000 });
        await page.screenshot({ path: `artifacts/desktop-${size}x${size}.png` });
        assert(await page.locator('.vehicle[data-w="2"][data-h="2"]').count(), 'Excavator visible');
        assert(await page.locator('.vehicle[data-w="4"][data-h="1"], .vehicle[data-w="1"][data-h="4"]').count(), 'Trailer visible');
        await page.getByRole('button', { name: 'ヒント', exact: true }).click();
        await page.locator('.vehicle.hint').waitFor();
        console.log(`PASS: ${size}x${size} expert generation, vehicle graphics, hint`);
    }
    await page.setViewportSize({ width: 850, height: 620 });
    await page.screenshot({ path: 'artifacts/desktop-small-window.png' });
    const box = await page.locator('#game-board').boundingBox();
    assert(box.x >= 0 && box.y >= 0 && box.x + box.width <= 850 && box.y + box.height <= 620, 'Board fits small window');
    await page.getByRole('button', { name: '遊び方', exact: true }).click();
    await page.getByRole('dialog').waitFor();
    await page.waitForTimeout(300);
    const modal = await page.getByRole('dialog').boundingBox();
    assert(modal.x >= 0 && modal.y >= 0 && modal.x + modal.width <= 850 && modal.y + modal.height <= 620, 'Help fits small window');
    await page.screenshot({ path: 'artifacts/desktop-help.png' });
    await page.keyboard.press('Escape');
    await page.getByRole('dialog').waitFor({ state: 'hidden' });
    console.log('PASS: small window, help dialog');
    await browser.close();
})().catch(error => { console.error(error); process.exitCode = 1; });
