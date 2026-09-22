const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium, } = require('./browser-launch.cjs').playwright();
const root = path.resolve(__dirname, '../../..');

(async () => {
    const browser = await chromium.launch(require('./browser-launch.cjs').launchOptions());
    try {
        const page = await browser.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        await page.route('http://picker.test/**', route => route.fulfill({ contentType: 'text/html', body: '<html><body></body></html>' }));
        await page.goto('http://picker.test/');
        await page.setContent(`<form id="record-edit-form" data-autosave-url="/save"><input name="RowVersion" value="1" type="hidden">
            <input name="Title" value="Case"><div data-call-picker data-url="/User/RecordCalls/Search" data-loading="Loading" data-error="Try again" data-empty="No matches" data-metadata="Numbers only" data-dates="Invalid dates">
            <select id="CallId" name="CallId" data-call-value><option value="">No call</option><option value="9" selected>Call #9</option></select>
            <details data-call-browser><summary>Browse calls</summary><div data-call-filters>
            <input type="search" maxlength="200" data-call-term aria-label="Search">
            <select data-call-status aria-label="Status"><option value="">All</option><option value="closed">Closed</option></select>
            <input type="date" data-call-from aria-label="From"><input type="date" data-call-to aria-label="To">
            <button type="button" data-call-search>Search</button></div><p role="status" data-call-message></p><div data-call-results></div>
            <button type="button" data-call-prev disabled>Previous</button><button type="button" data-call-next disabled>Next</button></details></div>
            <button type="submit" id="save">Save</button></form><p id="record-autosave-status"></p>`);
        await page.evaluate(() => {
            window.requests = []; window.saves = []; window.submits = 0;
            document.querySelector('form').addEventListener('submit', e => { e.preventDefault(); window.submits++; });
            window.fetch = async (url, options) => {
                if (String(url) === '/save') {
                    saves.push(Object.fromEntries(options.body.entries()));
                    return { ok: true, json: async () => ({ rowVersion: 2 }) };
                }
                const params = Object.fromEntries(new URL(url).searchParams.entries());
                requests.push(params);
                if (params.selectedId) return { ok: true, json: async () => ({ selected: { id: 9, text: 'Older closed call' } }) };
                if (params.term === 'failure') return { ok: false };
                if (params.term === 'slow') {
                    // Deliberately ignore abort: a completed stale server response must still be discarded.
                    await new Promise(resolve => setTimeout(resolve, 900));
                    return { ok: true, json: async () => ({ items: [{ id: 1, text: 'Stale' }], nextOffset: null }) };
                }
                const items = params.term === 'empty' ? [] : params.offset === '25' ? [{ id: 30, text: 'Historical fire' }]
                    : [{ id: 10, text: '<img src=x onerror="alert(1)"> New call' }];
                return { ok: true, json: async () => ({ items, nextOffset: params.offset === '25' ? null : 25, metadataOnly: params.term === 'protected' }) };
            };
        });
        await page.addScriptTag({ path: path.join(root, 'Web/Resgrid.Web/wwwroot/js/record-call-picker.js') });
        await page.addScriptTag({ path: path.join(root, 'Web/Resgrid.Web/wwwroot/js/record-authoring.js') });
        await page.waitForFunction(() => document.querySelector('#CallId').selectedOptions[0].textContent === 'Older closed call');
        assert.equal(await page.evaluate(() => requests.length), 1, 'Only selected call is loaded initially');
        await page.getByText('Browse calls', { exact: true }).click();
        await page.waitForSelector('[data-call-results] button');
        assert.equal(await page.locator('[data-call-results] img').count(), 0, 'Call text is never interpreted as HTML');
        await page.getByRole('button', { name: 'Next', exact: true }).click();
        await page.getByRole('button', { name: 'Historical fire', exact: true }).waitFor();
        assert.equal(await page.locator('#CallId').inputValue(), '9', 'Paging does not change selection');
        await page.getByRole('button', { name: 'Previous', exact: true }).click();
        await page.waitForSelector('[data-call-results] button');
        assert.equal(await page.getByRole('button', { name: 'Previous', exact: true }).isDisabled(), true);
        await page.getByLabel('Search', { exact: true }).fill('Main');
        await page.getByLabel('Search', { exact: true }).press('Enter');
        await page.getByLabel('Status').selectOption('closed');
        await page.getByLabel('From').fill('2020-01-01');
        await page.getByLabel('To').fill('2020-12-31');
        await page.waitForFunction(() => requests.at(-1).to === '2020-12-31');
        const request = await page.evaluate(() => requests.at(-1));
        assert.deepEqual(request, { term: 'Main', status: 'closed', from: '2020-01-01', to: '2020-12-31', offset: '0' });
        assert.equal(await page.evaluate(() => submits), 0, 'Enter in search must not submit the case/report');
        await page.waitForTimeout(2100);
        assert.equal(await page.evaluate(() => saves.length), 0, 'Search/filter changes must not autosave the report');
        await page.getByRole('button', { name: 'Next', exact: true }).click();
        await page.getByRole('button', { name: 'Historical fire', exact: true }).click();
        assert.equal(await page.locator('#CallId').inputValue(), '30');
        assert.equal(await page.locator('[data-call-browser]').getAttribute('open'), null);
        assert.equal(await page.evaluate(() => document.activeElement.id), 'CallId');
        await page.waitForFunction(() => saves.length === 1);
        assert.equal(await page.evaluate(() => saves[0].CallId), '30', 'Autosave receives the actual selected call ID');
        await page.getByText('Browse calls', { exact: true }).click();
        await page.getByLabel('Search', { exact: true }).fill('empty');
        await page.waitForFunction(() => document.querySelector('[data-call-message]').textContent === 'No matches');
        assert.equal(await page.locator('#CallId').inputValue(), '30', 'Empty searches preserve selection');
        assert.equal(await page.getByRole('button', { name: 'Next', exact: true }).isEnabled(), true, 'A filtered-out candidate page can still have a next page');
        await page.getByLabel('Search', { exact: true }).fill('failure');
        await page.waitForFunction(() => document.querySelector('[data-call-message]').textContent === 'Try again');
        assert.equal(await page.locator('#CallId').inputValue(), '30', 'Network failures preserve selection');
        await page.getByLabel('Search', { exact: true }).fill('slow');
        await page.waitForFunction(() => requests.at(-1).term === 'slow');
        await page.getByLabel('Search', { exact: true }).fill('protected');
        await page.waitForFunction(() => document.querySelector('[data-call-message]').textContent.includes('Numbers only'));
        await page.waitForTimeout(1000);
        assert.equal(await page.getByRole('button', { name: 'Stale', exact: true }).count(), 0);
        await page.getByLabel('From').fill('2021-01-01');
        await page.waitForFunction(() => document.querySelector('[data-call-message]').textContent === 'Invalid dates');
        assert.equal(await page.locator('[data-call-results] button').count(), 0);
        await page.locator('#CallId').selectOption('');
        assert.equal(await page.evaluate(() => new FormData(document.querySelector('form')).get('CallId')), '', 'Call remains optional and can be cleared');
        assert.equal(await page.evaluate(() => [...new FormData(document.querySelector('form')).keys()].some(k => ['term', 'status', 'from', 'to'].includes(k))), false);
        assert.deepEqual(errors, []);
        console.log('Call picker: browsing, search, filters, paging, selection, optional clear, autosave, escaping, stale responses, errors and keyboard passed.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
