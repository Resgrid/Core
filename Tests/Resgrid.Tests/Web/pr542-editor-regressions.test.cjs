const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { playwright, launchOptions } = require('./browser-launch.cjs');
const root = path.resolve(__dirname, '../../..');
const jquery = path.join(root, 'Web/Resgrid.Web/wwwroot/lib/jquery/dist/jquery.min.js');

(async () => {
    const browser = await playwright().chromium.launch(launchOptions());
    try {
        const page = await browser.newPage();
        for (const name of ['newcall', 'editcall', 'addArchivedCall']) {
            const source = fs.readFileSync(path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/internal/dispatch/resgrid.dispatch.' + name + '.js'), 'utf8');
            const handler = source.match(/\$\('#addNewLinkedCall'\)\.click\(function \(e\) \{([\s\S]*?)\n\s*\}\);/);
            assert.ok(handler, name + ': linked-call handler found');
            await page.setContent('<select id="selectLinkedCall"></select><input id="selectCallNote"><button id="addNewLinkedCall">Add</button><table id="linkedCalls"><tbody></tbody></table>');
            await page.addScriptTag({ path: jquery });
            await page.addScriptTag({ content: `
                window.selection = [{ id: '7' }];
                window.getText = (key, fallback) => fallback;
                $.fn.select2 = function (operation) { if (operation === 'data') return window.selection; if (operation === 'open') window.pickerOpened = true; return this; };
                ${handler[0]}
            ` });
            await page.locator('#selectCallNote').fill('Keep this note');
            await page.locator('#addNewLinkedCall').click();
            assert.equal(await page.locator('#linkedCalls tbody tr').count(), 1, name + ': missing label still adds the call');
            assert.equal(await page.locator('#linkedCall_7').inputValue(), '7');
            assert.equal(await page.locator('#linkedCalls td').nth(1).textContent(), 'Keep this note');
            await page.locator('#addNewLinkedCall').click();
            assert.equal(await page.locator('#linkedCalls tbody tr').count(), 1, name + ': duplicate call is not added');
            await page.evaluate(() => { window.selection = [{ id: '8', text: '<img src=x onerror=alert(1)>' }]; });
            await page.locator('#addNewLinkedCall').click();
            assert.equal(await page.locator('#linkedCalls img').count(), 0, name + ': labels stay text');
            assert.equal(await page.locator('#linkedCalls tr').nth(1).locator('td').first().textContent(), '<img src=x onerror=alert(1)>');
            await page.evaluate(() => { window.selection = []; });
            await page.locator('#addNewLinkedCall').click();
            assert.equal(await page.evaluate(() => window.pickerOpened), true, name + ': empty selection opens the picker');
        }

        // Execute the production trigger renderer and add/remove handlers with the empty card produced by detached-reference cleanup.
        const editor = fs.readFileSync(path.join(root, 'Web/Resgrid.Web/Areas/User/Views/RunCards/Edit.cshtml'), 'utf8');
        const start = editor.indexOf('function renderTriggers()');
        const end = editor.indexOf("$('#triggersTable').on('change'", start);
        assert.ok(start >= 0 && end > start, 'trigger editor block found');
        // Use the production markup (Razor text is harmless here) so the warning must be visible even while General is the active tab.
        await page.setContent(editor.slice(editor.indexOf('<div class="row wrapper'), editor.indexOf('@section Scripts')));
        await page.addStyleTag({ content: '.tab-pane { display: none; } .tab-pane.active { display: block; }' });
        await page.addScriptTag({ path: jquery });
        await page.addScriptTag({ content: `var card = { triggers: [] }; var lookups = { priorities: [], callTypes: [] }; function optionList() { return ''; } ${editor.slice(start, end)} renderTriggers();` });
        assert.equal(await page.locator('#saveCardButton').isDisabled(), true);
        assert.equal(await page.locator('#triggerRequired').isVisible(), true);
        await page.evaluate(() => { $('#tab-general').removeClass('active'); $('#tab-triggers').addClass('active'); });
        await page.locator('#addTriggerButton').click();
        assert.equal(await page.locator('#saveCardButton').isEnabled(), true);
        assert.equal(await page.locator('#triggerRequired').isVisible(), false);
        await page.locator('.remove-trigger').click();
        assert.equal(await page.locator('#saveCardButton').isDisabled(), true);
        assert.equal(await page.locator('#triggerRequired').isVisible(), true);

        const errorHandler = editor.match(/error: function \(xhr\) \{([\s\S]*?)\n\s*\}/);
        assert.ok(errorHandler, 'save failure handler found');
        await page.addScriptTag({ content: `window.saveFailure = function (xhr) { ${errorHandler[1]} };` });
        await page.evaluate(() => window.saveFailure({ responseJSON: { message: 'Reload the editor <img src=x>' } }));
        assert.equal(await page.locator('#saveError').textContent(), 'Reload the editor <img src=x>');
        assert.equal(await page.locator('#saveError img').count(), 0);
        await page.evaluate(() => window.saveFailure({}));
        assert.equal(await page.locator('#saveError').textContent(), 'Save failed.');
        console.log('PR 542 editor regressions passed (three linked-call pickers, trigger validation, save failures).');
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
