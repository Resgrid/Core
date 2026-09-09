const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('./browser-launch.cjs').playwright();
const script = path.resolve(__dirname, '../../../Web/Resgrid.Web/wwwroot/js/app/internal/checklists/checklist-live.js');
(async () => {
    const browser = await chromium.launch(require('./browser-launch.cjs').launchOptions());
    try {
        const page = await browser.newPage(); const errors = [];
        page.on('pageerror', e => errors.push(e.message));
        await page.setContent('<form id="checklist-live-refresh"><input type="hidden" name="__RequestVerificationToken" value="csrf"><input type="hidden" name="__ResgridProtectedGrant" value="synthetic-grant"></form><textarea aria-label="Excuse"></textarea>');
        await page.evaluate(() => {
            window.$ = work => work(); window.resgrid = { common: { signalr: { init: () => {} } } }; window.submissions = [];
            document.querySelector('form').addEventListener('submit', event => { event.preventDefault(); submissions.push(Object.fromEntries(new FormData(event.target))); });
        });
        await page.addScriptTag({ path: script });
        await page.evaluate(() => { document.dispatchEvent(new Event('resgrid:checklists-updated')); document.dispatchEvent(new Event('resgrid:checklists-updated')); });
        await page.waitForFunction(() => submissions.length === 1);
        assert.deepEqual(await page.evaluate(() => submissions[0]), { __RequestVerificationToken: 'csrf', __ResgridProtectedGrant: 'synthetic-grant' });
        await page.getByLabel('Excuse').fill('Keep this unsaved reason');
        await page.evaluate(() => document.dispatchEvent(new Event('resgrid:checklists-updated')));
        await page.waitForTimeout(600); assert.equal(await page.evaluate(() => submissions.length), 1);
        await page.getByLabel('Excuse').fill(''); await page.getByLabel('Excuse').blur();
        await page.waitForFunction(() => submissions.length === 2);
        await page.evaluate(() => {
            document.querySelector('form').remove(); document.querySelector('textarea').remove();
            document.body.insertAdjacentHTML('beforeend', '<input type="checkbox" id="include-checklists" checked>');
            window.refreshes = 0; resgrid.calendar = { index: { getCalendar: () => ({ refetchEvents: () => refreshes++ }) } };
            document.dispatchEvent(new Event('resgrid:checklists-updated'));
        });
        await page.waitForFunction(() => refreshes === 1); assert.deepEqual(errors, []);
        console.log('Checklist live updates coalesce, retain CSRF/ADP context, preserve unsaved text and refresh the calendar.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
