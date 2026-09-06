const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require(process.env.RESGRID_PLAYWRIGHT_PATH || 'playwright');
const root = path.resolve(__dirname, '../../..');
(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    try {
        const page = await browser.newPage();
        await page.clock.install();
        await page.setContent(`<form id="record-edit-form" data-autosave-url="/save"><input name="RecordId" value="record"><input name="RowVersion" value="4"><input name="__RequestVerificationToken" value="fixture-token"><textarea name="Details.Narrative">Original</textarea><template id="record-narrative-initial"><p>Original <strong>formatted</strong></p></template><div id="record-narrative-editor" hidden></div><input name="files" type="file"><button id="manual" type="submit">Save Draft</button></form><p id="record-autosave-status"></p>`);
        await page.evaluate(() => {
            window.requests = [];
            window.fetch = (url, options) => new Promise(resolve => window.requests.push({ url, values: Array.from(options.body.entries()), resolve }));
        });
        await page.addScriptTag({ path: path.join(root, 'Web/Resgrid.Web/wwwroot/lib/quill/dist/quill.min.js') });
        await page.addScriptTag({ path: path.join(root, 'Web/Resgrid.Web/wwwroot/js/record-authoring.js') });
        assert.match(await page.locator('.ql-editor').innerHTML(), /<strong>formatted<\/strong>/);
        await page.locator('.ql-editor').fill('First edit');
        await page.clock.fastForward(2100);
        assert.equal(await page.evaluate(() => requests.length), 1);
        await page.locator('.ql-editor').fill('Second edit while saving');
        await page.clock.fastForward(2100);
        assert.equal(await page.evaluate(() => requests.length), 1, 'Only one save may be in flight');
        await page.evaluate(() => requests[0].resolve({ ok: true, status: 200, json: async () => ({ rowVersion: 5 }) }));
        assert.equal(await page.locator('[name=RowVersion]').inputValue(), '5');
        await page.clock.fastForward(2100);
        assert.equal(await page.evaluate(() => requests.length), 2);
        const second = Object.fromEntries(await page.evaluate(() => requests[1].values));
        assert.equal(second.RowVersion, '5'); assert.match(second['Details.Narrative'], /Second edit while saving/);
        assert.equal(second.__RequestVerificationToken, 'fixture-token'); assert.equal(second.files, undefined);
        await page.evaluate(() => requests[1].resolve({ ok: false, status: 409, json: async () => ({ error: 'Conflict; reload before saving.' }) }));
        await page.locator('.ql-editor').fill('Keep this unsaved text');
        await page.clock.fastForward(5000);
        assert.equal(await page.evaluate(() => requests.length), 2, 'A conflict must stop automatic retries');
        await page.locator('#manual').click();
        assert.match(await page.locator('#record-autosave-status').innerText(), /Saving stopped/);
        assert.match(await page.locator('.ql-editor').innerText(), /Keep this unsaved text/);
        console.log('PASS: rich text initialization; serialized autosave; latest edit/version preservation; antiforgery; file exclusion; conflict stops retry/manual overwrite.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
