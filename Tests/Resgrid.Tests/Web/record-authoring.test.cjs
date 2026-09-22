const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('./browser-launch.cjs').playwright();
const root = path.resolve(__dirname, '../../..');
(async () => {
    const browser = await chromium.launch(require('./browser-launch.cjs').launchOptions());
    try {
        const page = await browser.newPage();
        await page.clock.install();
        await page.setContent(`<form id="record-edit-form" class="form-horizontal" data-autosave-url="/save"><input type="hidden" name="RecordId" value="record"><input type="hidden" name="RowVersion" value="4"><input type="hidden" name="__RequestVerificationToken" value="fixture-token"><div class="form-group"><label class="col-sm-2 control-label">Location</label><div class="col-sm-10"><input id="location" class="form-control"></div></div><div class="form-group"><label class="col-sm-2 control-label">Narrative</label><div class="col-sm-10"><textarea class="form-control" rows="8" name="Details.Narrative">Original</textarea><template id="record-narrative-initial"><p>Original <strong>formatted</strong></p></template><div id="record-narrative-editor" style="min-height:200px" hidden></div></div></div><input name="files" type="file"><button id="manual" type="submit">Save Draft</button></form><p id="record-autosave-status"></p>`);
        for (const stylesheet of ['lib/bootstrap/dist/css/bootstrap.css', 'css/style.css', 'lib/quill/dist/quill.snow.css']) {
            await page.addStyleTag({ path: path.join(root, 'Web/Resgrid.Web/wwwroot', stylesheet) });
        }
        // Keep a usable plain-text fallback when the editor library is unavailable.
        await page.addScriptTag({ path: path.join(root, 'Web/Resgrid.Web/wwwroot/js/record-authoring.js') });
        assert.equal(await page.locator('[name="Details.Narrative"]').isVisible(), true);
        assert.equal(await page.locator('#record-narrative-editor').isVisible(), false);
        // Clone the form to remove fallback listeners before initializing with Quill.
        await page.evaluate(() => { const form = document.getElementById('record-edit-form'); form.replaceWith(form.cloneNode(true)); });
        await page.evaluate(() => {
            window.requests = [];
            window.fetch = (url, options) => new Promise(resolve => window.requests.push({ url, values: Array.from(options.body.entries()), resolve }));
        });
        await page.addScriptTag({ path: path.join(root, 'Web/Resgrid.Web/wwwroot/lib/quill/dist/quill.min.js') });
        await page.addScriptTag({ path: path.join(root, 'Web/Resgrid.Web/wwwroot/js/record-authoring.js') });
        assert.equal(await page.locator('[name="Details.Narrative"]').isVisible(), false, 'The original textbox must be hidden with the application styles loaded');
        for (const width of [1440, 768, 390]) {
            await page.setViewportSize({ width, height: 900 });
            const location = await page.locator('#location').boundingBox();
            const toolbar = await page.locator('.ql-toolbar').boundingBox();
            const editor = await page.locator('#record-narrative-editor').boundingBox();
            assert.ok(Math.abs(toolbar.x - location.x) < 1 && Math.abs(editor.x - location.x) < 1, `Editor aligns with the fields at ${width}px`);
            assert.ok(Math.abs(editor.width - location.width) < 1 && Math.abs(toolbar.width - editor.width) < 1, `Editor fills the field column at ${width}px`);
            assert.ok(Math.abs(toolbar.y + toolbar.height - editor.y) < 1, `Toolbar sits directly above the editor at ${width}px`);
        }
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
        console.log('PASS: plain-text fallback; rich text visibility and responsive alignment; serialized autosave; latest edit/version preservation; antiforgery; file exclusion; conflict stops retry/manual overwrite.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
