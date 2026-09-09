const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { playwright, launchOptions } = require('./browser-launch.cjs');
const scripts = path.resolve(__dirname, '../../../Web/Resgrid.Web/wwwroot/js/app/internal');

(async () => {
    const browser = await playwright().chromium.launch(launchOptions());
    try {
        const page = await browser.newPage();
        await page.setContent(`<form class="m4-lines-form" data-receipt="true"><div class="m4-lines"><div class="m4-line">
            <input name="Lines[0].PurchaseOrderItemId" value="line-1">
            <select name="Lines[0].LocationId"><option value="first">First</option><option value="chosen">Chosen</option></select>
            <select name="Lines[0].LotId"><option value="">None</option><option value="lot-chosen">Chosen lot</option></select>
            <input name="Lines[0].Quantity" value="8"><input name="Lines[0].Asset.SerialNumber" value="serial">
            <button type="button" class="m4-copy-line">Copy</button><button type="button" class="m4-remove-line">Remove</button>
            </div></div></form>`);
        await page.addScriptTag({ path: path.join(scripts, 'inventory/inventory-purchasing.js') });
        await page.locator('[name="Lines[0].LocationId"]').selectOption('chosen');
        await page.locator('[name="Lines[0].LotId"]').selectOption('lot-chosen');
        await page.locator('.m4-copy-line').click();
        assert.equal(await page.locator('[name="Lines[1].LocationId"]').inputValue(), 'chosen');
        assert.equal(await page.locator('[name="Lines[1].LotId"]').inputValue(), 'lot-chosen');
        assert.equal(await page.locator('[name="Lines[1].PurchaseOrderItemId"]').inputValue(), 'line-1');
        assert.equal(await page.locator('[name="Lines[1].Quantity"]').inputValue(), '1');
        assert.equal(await page.locator('[name="Lines[1].Asset.SerialNumber"]').inputValue(), '');
    } finally { await browser.close(); }

    const requests = []; let hidden = true, reloads = 0;
    const $ = selector => ({
        ready: callback => callback(), on: () => {}, val: () => 'csrf',
        prop: (name, value) => { if (selector === '#reporting-error' && name === 'hidden') hidden = value; },
        DataTable: () => ({ ajax: { reload: () => reloads++ } })
    });
    $.ajax = options => {
        const request = { options, done(callback) { this.success = callback; return this; }, fail(callback) { this.failure = callback; return this; } };
        requests.push(request); return request;
    };
    const context = { $, document: {}, resgrid: { absoluteBaseUrl: '' }, confirm: () => true };
    vm.runInNewContext(fs.readFileSync(path.join(scripts, 'profile/resgrid.profile.reporting.js'), 'utf8'), context);
    for (const action of ['activateSchedule', 'deactivateSchedule', 'deleteSchedule']) {
        context.resgrid.profile.reporting[action](42);
        const request = requests.at(-1);
        assert.equal(request.options.type, 'POST');
        assert.equal(request.options.data.__RequestVerificationToken, 'csrf');
        hidden = true; const previousReloads = reloads;
        request.failure({ status: 403, responseText: '<script>unsafe()</script>' });
        assert.equal(hidden, false); assert.equal(reloads, previousReloads);
        request.success(); assert.equal(hidden, true); assert.equal(reloads, previousReloads + 1);
        request.failure({ status: 401 }); assert.equal(hidden, true);
    }
    console.log('PR507 receipt copying and all three schedule failure handlers passed.');
})().catch(error => { console.error(error); process.exitCode = 1; });
