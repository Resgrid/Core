const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('./browser-launch.cjs').playwright();
const root = path.resolve(__dirname, '../../..');

function row(index, code = '', starts = '', ends = '') {
    return `<div class="well well-sm meal-eligibility-row"><div class="row">${[
        ['MealCode', 'Meal code', 'text', code],
        ['StartsBefore', 'Shift starts at or before', 'time', starts],
        ['EndsAfter', 'Shift ends at or after', 'time', ends]
    ].map(([field, label, type, value]) => `<div class="col-sm-4"><label for="MealEligibility_${index}__${field}">${label}</label><input id="MealEligibility_${index}__${field}" name="MealEligibility[${index}].${field}" data-meal-field="${field}" class="form-control" type="${type}" value="${value}" ${type === 'time' ? 'step="60"' : 'maxlength="10"'}><span data-valmsg-for="MealEligibility[${index}].${field}"></span></div>`).join('')}</div><button type="button" class="btn btn-default btn-xs m-t-sm" data-remove-meal-eligibility>Remove meal rule</button></div>`;
}

(async () => {
    const browser = await chromium.launch(require('./browser-launch.cjs').launchOptions());
    try {
        const page = await browser.newPage({ viewport: { width: 1000, height: 800 } });
        await page.setContent(`<form><fieldset><div style="max-width:650px;padding:20px"><h3>Meal eligibility</h3><p>Add a rule for each meal code (for example, B for breakfast). Enter local shift cutoff times; leave a time blank for no limit.</p><div id="meal-eligibility-rows"></div><p id="meal-eligibility-empty">No meal eligibility rules. Add a rule to set time limits.</p><button type="button" class="btn btn-default btn-sm" id="add-meal-eligibility">Add meal rule</button><template id="meal-eligibility-template">${row('__index__')}</template></div></fieldset></form>`);
        await page.addStyleTag({ path: path.join(root, 'Web/Resgrid.Web/wwwroot/lib/bootstrap/dist/css/bootstrap.css') });
        await page.addScriptTag({ path: path.join(root, 'Web/Resgrid.Web/wwwroot/js/meal-eligibility.js') });
        const rows = page.locator('.meal-eligibility-row');
        const add = page.getByRole('button', { name: 'Add meal rule' });
        assert.equal(await page.locator('#meal-eligibility-empty').isVisible(), true);
        for (const [code, starts, ends] of [['B', '07:00', ''], ['L', '12:00', '13:00'], ['D', '', '19:00']]) {
            await add.click();
            assert.equal(await page.evaluate(() => document.activeElement.dataset.mealField), 'MealCode');
            await rows.last().getByLabel('Meal code').fill(code);
            await rows.last().getByLabel('Shift starts at or before').fill(starts);
            await rows.last().getByLabel('Shift ends at or after').fill(ends);
        }
        assert.equal(await page.locator('#meal-eligibility-empty').isVisible(), false);
        await rows.nth(1).getByRole('button', { name: 'Remove meal rule' }).click();
        assert.equal(await rows.count(), 2);
        assert.equal(await page.locator('[name="MealEligibility[1].MealCode"]').inputValue(), 'D');
        assert.equal(await page.locator('[name="MealEligibility[1].EndsAfter"]').inputValue(), '19:00');
        assert.equal(await page.evaluate(() => document.activeElement.name), 'MealEligibility[1].MealCode');
        const posted = await page.evaluate(() => Object.fromEntries(new FormData(document.querySelector('form'))));
        assert.deepEqual(posted, {
            'MealEligibility[0].MealCode': 'B', 'MealEligibility[0].StartsBefore': '07:00', 'MealEligibility[0].EndsAfter': '',
            'MealEligibility[1].MealCode': 'D', 'MealEligibility[1].StartsBefore': '', 'MealEligibility[1].EndsAfter': '19:00'
        });
        await add.click();
        await rows.last().getByLabel('Meal code').fill('Night');
        await rows.last().getByLabel('Shift starts at or before').fill('23:00');
        await rows.last().getByLabel('Shift ends at or after').fill('01:00');
        assert.equal(await page.evaluate(() => document.querySelector('form').checkValidity()), true);
        assert.equal(await page.evaluate(() => {
            const inputs = [...document.querySelectorAll('[data-meal-field]')];
            return new Set(inputs.map(i => i.id)).size === inputs.length && inputs.every(i => i.labels.length === 1);
        }), true, 'Added and reindexed controls retain unique labels');
        await page.setViewportSize({ width: 390, height: 900 });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true, 'The editor fits a phone viewport');
        if (process.env.RESGRID_MEAL_SCREENSHOT) await page.screenshot({ path: process.env.RESGRID_MEAL_SCREENSHOT, fullPage: true });
        await page.locator('fieldset').evaluate(el => { el.disabled = true; });
        assert.equal(await add.isDisabled(), true);
        assert.equal(await rows.first().getByLabel('Meal code').isDisabled(), true);
        await page.locator('fieldset').evaluate(el => { el.disabled = false; });
        while (await rows.count()) await rows.first().getByRole('button', { name: 'Remove meal rule' }).click();
        assert.equal(await page.locator('#meal-eligibility-empty').isVisible(), true);
        assert.equal(await page.evaluate(() => document.activeElement.id), 'add-meal-eligibility');
        assert.deepEqual(await page.evaluate(() => [...new FormData(document.querySelector('form'))]), []);
        console.log('Meal eligibility: add, remove, indexed form submission, overnight times, labels, mobile layout and disabled controls passed.');
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exit(1); });
