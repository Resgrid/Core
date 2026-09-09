const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('./browser-launch.cjs').playwright();
const root = path.resolve(__dirname, '../../..');
const script = path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/internal/checklists/checklists.js');
const cultures = [...fs.readFileSync(path.join(root, 'Core/Resgrid.Localization/SupportedLocales.cs'), 'utf8').matchAll(/\{"([a-z]{2})",/g)].map(match => match[1]);
const json = value => JSON.stringify(value).replace(/</g, '\\u003c');
const id = '11111111-1111-1111-1111-111111111111';
const makeItem = (suffix, type) => ({ Id: suffix.padStart(8, '0') + '-1111-1111-1111-111111111111', Name: 'User-authored question ' + suffix, Type: type, Required: true, Critical: false, AllowNotApplicable: true, Weight: 1, Options: [] });

(async () => {
    const browser = await chromium.launch(require('./browser-launch.cjs').launchOptions());
    try {
        assert.ok(cultures.length > 0);
        assert.ok(cultures.includes('ar'), 'Arabic is part of the supported locale registry');
        for (const culture of cultures) {
            const page = await browser.newPage();
            const errors = []; page.on('pageerror', error => errors.push(error.message));
            await page.route('https://checklist-locales.test/**', route => route.fulfill({ contentType: 'text/html', body: '<html><body></body></html>' }));
            await page.goto('https://checklist-locales.test/');
            const resourceXml = fs.readFileSync(path.join(root, 'Core/Resgrid.Localization/Areas/User/Checklists/Checklists.' + culture + '.resx'), 'utf8');
            const translations = await page.evaluate(xml => Object.fromEntries([...new DOMParser().parseFromString(xml, 'application/xml').querySelectorAll('data')].map(entry => [entry.getAttribute('name'), entry.querySelector('value').textContent])), resourceXml);
            const translate = key => { assert.ok(translations[key], culture + ': missing ' + key); return translations[key]; };
            const first = { ...makeItem('1', 0), Critical: true };
            const custom = { ...makeItem('2', 6), Options: ['Yes', 'No'], PassingValue: 'Yes', VisibleWhen: { ItemId: first.Id, EqualsValue: 'fail' } };
            const definition = { Name: 'User-owned title', Category: 0, TargetType: 0, PassThreshold: 100, Sections: [{ Id: id, Name: 'User-owned section', Items: [first, custom] }] };
            const localeScript = `<script id="checklist-translations" type="application/json">${json(translations)}</script>`;
            await page.setContent(`<html lang="${culture}"><body><div id="checklist-error" hidden></div><form id="checklist-editor" action="/save"><input name="formJson"><div id="checklist-builder"></div><button type="submit" id="save">${translate('SaveDraft')}</button></form><script id="checklist-form-data" type="application/json">${json(definition)}</script>${localeScript}</body></html>`);
            await page.evaluate(() => { window.saved = null; window.fetch = async (_, options) => { saved = JSON.parse(options.body.get('formJson')); return { ok: true, json: async () => ({ revision: 2 }) }; }; });
            await page.addScriptTag({ path: script });
            assert.equal(await page.getByLabel(translate('Checklist name'), { exact: true }).inputValue(), definition.Name);
            assert.equal(await page.getByLabel(translate('Answer type'), { exact: true }).first().locator('option:checked').textContent(), translate('Pass / Fail'));
            assert.equal(await page.getByLabel(translate('Category'), { exact: true }).locator('option:checked').textContent(), translate('Start of shift'));
            const condition = page.getByLabel(translate('Answer that activates this condition'), { exact: true });
            assert.equal(await condition.inputValue(), 'fail');
            assert.equal(await condition.locator('option:checked').textContent(), translate('Fail'));
            await condition.selectOption('pass');
            await page.locator('#save').click();
            assert.equal(await page.evaluate(() => saved.Sections[0].Items[1].VisibleWhen.EqualsValue), 'pass', 'Translated condition labels preserve protocol values');
            assert.deepEqual(await page.evaluate(() => saved.Sections[0].Items[1].Options), ['Yes', 'No'], 'User-defined choices are not translated');

            const reading = { ...makeItem('3', 3), Units: 'kg', Minimum: 1, Maximum: 5 };
            const signature = makeItem('4', 9);
            const run = { Completion: { Id: id }, Form: { ...definition, RequireLocation: true, Sections: [{ Id: id, Name: definition.Sections[0].Name, Items: [first, reading, { ...custom, VisibleWhen: null }, signature] }] }, Input: { Revision: 1, Answers: [] }, Files: [] };
            await page.setContent(`<html lang="${culture}"><body><div id="checklist-error" hidden></div><div id="checklist-saved" hidden></div><form id="checklist-run" action="/run"><input name="inputJson"><div id="checklist-answers"></div><button type="submit" id="save">${translate('SaveProgress')}</button></form><script id="checklist-run-data" type="application/json">${json(run)}</script>${localeScript}</body></html>`);
            await page.evaluate(() => { window.saved = null; window.fetch = async (_, options) => { saved = JSON.parse(options.body.get('inputJson')); return { ok: true, json: async () => ({ revision: 2 }) }; }; });
            await page.addScriptTag({ path: script });
            await page.getByLabel(translate('Answer status'), { exact: true }).first().selectOption('1');
            const answer = page.getByLabel(translate('Answer'), { exact: true }).first();
            await answer.selectOption('fail');
            assert.equal(await answer.locator('option:checked').textContent(), translate('Fail'));
            const customAnswer = page.getByLabel(translate('Answer'), { exact: true }).nth(1);
            assert.equal(await customAnswer.locator('option[value="Yes"]').textContent(), 'Yes');
            assert.equal(await page.getByLabel(translate('Answer') + ' (kg)', { exact: true }).count(), 1);
            assert.equal(await page.getByLabel(translate('Reported latitude (required)'), { exact: true }).count(), 1);
            assert.equal(await page.getByLabel(translate('Signature drawing area. Alternatively upload an image.'), { exact: true }).count(), 1);
            await page.getByRole('button', { name: translate('Save signature image'), exact: true }).click();
            assert.equal(await page.locator('#checklist-error').textContent(), translate('Draw a signature first.'));
            await page.locator('#save').click();
            await page.locator('#checklist-saved:not([hidden])').waitFor();
            assert.equal(await page.locator('#checklist-saved').textContent(), translate('Progress saved.'));
            assert.equal(await page.evaluate(() => saved.Answers[0].Value), 'fail');
            assert.deepEqual(errors, [], culture);
            await page.close();
        }
        console.log('PASS: all supported languages render translated checklist controls, condition choices, required labels, signature accessibility, errors and save notices; response codes and user-authored text are preserved.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
