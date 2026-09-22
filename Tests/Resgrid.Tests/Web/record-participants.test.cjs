const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('./browser-launch.cjs').playwright();
const root = path.resolve(__dirname, '../../..');

function row(index, user = '', unit = '', role = '') {
    return `<tr><td><input type="hidden" name="ParticipantRows[${index}].Selected" value="true"><select id="ParticipantRows_${index}__UserId" name="ParticipantRows[${index}].UserId"><option value="">Choose a participant</option>${['P1', 'P2', 'P3'].map(id => `<option value="${id}" ${id === user ? 'selected' : ''}>${id}</option>`).join('')}</select></td><td><select name="ParticipantRows[${index}].UnitId"><option value="">No assigned unit</option><option value="5" ${unit === '5' ? 'selected' : ''}>Engine 5</option></select></td><td><input name="ParticipantRows[${index}].Role" value="${role}"></td><td><button type="button" data-remove-record-participant>Remove</button></td></tr>`;
}

(async () => {
    const browser = await chromium.launch(require('./browser-launch.cjs').launchOptions());
    try {
        const page = await browser.newPage();
        await page.clock.install();
        await page.setContent(`<form id="record-edit-form" data-autosave-url="/save"><input type="hidden" name="RowVersion" value="4"><input type="hidden" name="__RequestVerificationToken" value="fixture-token"><table><tbody id="record-participant-rows">${row(0, 'P1')}${row(1, 'P2')}${row(2, 'P3', '5', 'Instructor')}</tbody></table><button type="button" id="add-record-participant">Add participant</button><template id="record-participant-template">${row('__index__')}</template><button id="save" type="submit">Save Draft</button></form><p id="record-autosave-status"></p>`);
        await page.evaluate(() => {
            window.requests = [];
            window.fetch = async (_, options) => {
                window.requests.push(Object.fromEntries(options.body.entries()));
                return { ok: true, status: 200, json: async () => ({ rowVersion: 4 + window.requests.length }) };
            };
        });
        await page.addScriptTag({ path: path.join(root, 'Web/Resgrid.Web/wwwroot/js/record-authoring.js') });
        await page.addScriptTag({ path: path.join(root, 'Web/Resgrid.Web/wwwroot/js/record-participants.js') });
        const rows = page.locator('#record-participant-rows tr');

        await rows.nth(1).getByRole('button', { name: 'Remove' }).click();
        assert.equal(await rows.count(), 2, 'Removing an existing participant immediately removes its row');
        assert.equal(await page.locator('[name="ParticipantRows[1].UserId"]').inputValue(), 'P3');
        assert.equal(await page.locator('[name="ParticipantRows[1].UnitId"]').inputValue(), '5');
        assert.equal(await page.locator('[name="ParticipantRows[1].Role"]').inputValue(), 'Instructor');
        assert.equal(await page.evaluate(() => document.activeElement.name), 'ParticipantRows[1].UserId');
        await page.clock.fastForward(2100);
        const saved = await page.evaluate(() => requests[0]);
        assert.equal(saved['ParticipantRows[0].UserId'], 'P1');
        assert.equal(saved['ParticipantRows[1].UserId'], 'P3');
        assert.equal(saved['ParticipantRows[1].Selected'], 'true');
        assert.equal(saved['ParticipantRows[2].UserId'], undefined, 'Posting indices stay contiguous');
        assert.equal(saved.__RequestVerificationToken, 'fixture-token');

        await rows.first().getByRole('button', { name: 'Remove' }).click();
        assert.equal(await page.locator('#ParticipantRows_0__UserId').inputValue(), 'P3');
        await page.getByRole('button', { name: 'Add participant' }).click();
        assert.equal(await page.evaluate(() => document.activeElement.name), 'ParticipantRows[1].UserId');
        await page.locator('[name="ParticipantRows[1].UserId"]').selectOption('P2');
        await rows.last().getByRole('button', { name: 'Remove' }).click();
        assert.equal(await rows.count(), 1, 'Newly added rows can also be removed');
        await rows.first().getByRole('button', { name: 'Remove' }).click();
        assert.equal(await rows.count(), 0);
        assert.equal(await page.evaluate(() => document.activeElement.id), 'add-record-participant');
        await page.clock.fastForward(2100);
        assert.equal(await page.evaluate(() => Object.keys(requests.at(-1)).some(key => key.startsWith('ParticipantRows['))), false, 'Removing the last participant autosaves an empty participant list');

        await page.getByRole('button', { name: 'Add participant' }).click();
        await page.locator('[name="ParticipantRows[0].UserId"]').selectOption('P1');
        await page.locator('[name="ParticipantRows[0].Role"]').fill('Attendee');
        await page.evaluate(() => document.getElementById('record-edit-form').addEventListener('submit', event => {
            event.preventDefault();
            window.manualValues = Object.fromEntries(new FormData(event.target));
        }));
        await page.locator('#save').click();
        const manual = await page.evaluate(() => manualValues);
        assert.equal(manual['ParticipantRows[0].Selected'], 'true');
        assert.equal(manual['ParticipantRows[0].UserId'], 'P1');
        assert.equal(manual['ParticipantRows[0].Role'], 'Attendee');
        assert.equal(manual['ParticipantRows[1].UserId'], undefined);
        console.log('PASS: remove existing/new/all participants; contiguous form indices; remaining unit/role preservation; keyboard focus; autosave and manual submission.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
