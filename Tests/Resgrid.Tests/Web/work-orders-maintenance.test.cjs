const assert = require('node:assert/strict');
const path = require('node:path');
const launch = require('./browser-launch.cjs');
(async () => {
    const browser = await launch.playwright().chromium.launch(launch.launchOptions());
    try {
        const page = await browser.newPage(); const errors = [];
        page.on('pageerror', e => errors.push(e.message));
        const pickers = ['item','location','lot','asset'].map(kind => `<select class="inventory-part-choice" data-kind="${kind}"><option value="">â€”</option></select><button type="button" class="inventory-part-load" data-kind="${kind}">Charger</button>`).join('');
        await page.setContent(`<p class="work-order-error" hidden></p><div class="work-order-protected">
        <form class="work-order-form work-order-command" data-destination="Recurrence" data-step-prefix="Input.Template.Content.Steps" action="https://example.invalid/save">
        <input name="__RequestVerificationToken" value="synthetic-csrf"/>
        <div id="work-order-steps"></div><button type="button" id="work-order-add-step">Ã‰tape</button>
        <input type="checkbox" class="maintenance-weekday" value="1"/><input type="checkbox" class="maintenance-weekday" value="5"/><input id="maintenance-weekdays" value="0"/>
        <div class="work-order-inventory" data-url="https://example.invalid/choices">${pickers}</div><div id="allocation-destination" class="work-order-inventory" data-url="https://example.invalid/choices"><select class="inventory-part-choice" data-kind="location"><option value="">—</option></select><button type="button" class="inventory-part-load" data-kind="location">Charger</button></div><button type="submit">Enregistrer</button></form></div>
        <script id="work-order-page" type="application/json">{"protectedData":true,"grant":"synthetic-grant","expiry":"2030-01-01T00:00:00Z","index":"about:blank#concealed","reopen":"/reopen","error":"Erreur"}</script>`);
        await page.evaluate(() => {
            window.$ = f => f(); window.requests = []; window.navigations = [];
            window.resgridAdpReveal = { bindForm() {}, applyGrantHeader(h) { h.set('X-Test-Grant', 'synthetic-grant'); } };
            window.fetch = async (url, options) => {
                const data = Object.fromEntries(options.body); requests.push({data, cache:options.cache, credentials:options.credentials, grant:options.headers?.get('X-Test-Grant')});
                if (url.endsWith('/save')) return {ok:true,json:async()=>({id:42})};
                if (data.kind === 'asset') return new Promise(resolve => { window.completeChoices = () => resolve({ok:true,json:async()=>({items:[{id:'late',name:'Private late result'}],hasMore:false})}); });
                return {ok:true,json:async()=>({items:[{id:'item-'+data.page,name:'<img src=x onerror="window.xss=true">'}],hasMore:data.page==='0'})};
            };
            HTMLFormElement.prototype.submit = function () { navigations.push(Object.fromEntries(new FormData(this))); };
        });
        await page.addScriptTag({path:path.resolve(__dirname,'../../../Web/Resgrid.Web/wwwroot/js/app/internal/workorders/work-orders.js')});
        await page.click('#allocation-destination button');
        assert.equal(await page.locator('#allocation-destination select option').count(),2);
        await page.click('#work-order-add-step');
        assert.equal(await page.locator('[name="Input.Template.Content.Steps[0].Text"]').count(),1);
        for (const checkbox of await page.locator('.maintenance-weekday').all()) await checkbox.check();
        assert.equal(await page.locator('#maintenance-weekdays').inputValue(),'34');
        await page.click('button[data-kind=item]'); await page.click('button[data-kind=item]');
        assert.equal(await page.locator('select[data-kind=item] option').count(),3);
        assert.equal(await page.locator('button[data-kind=item]').isDisabled(),true);
        assert.equal(await page.locator('.work-order-inventory img').count(),0);
        assert.equal(await page.evaluate(()=>window.xss),undefined);
        const request = await page.evaluate(()=>requests[0]);
        assert.equal(request.data.__ResgridProtectedGrant,'synthetic-grant'); assert.equal(request.data.__RequestVerificationToken,'synthetic-csrf');
        assert.equal(request.cache,'no-store'); assert.equal(request.credentials,'same-origin'); assert.equal(request.grant,'synthetic-grant');
        await page.selectOption('select[data-kind=item]','item-0'); await page.click('button[data-kind=lot]');
        await page.selectOption('select[data-kind=item]','item-1');
        assert.equal(await page.locator('select[data-kind=lot] option').count(),1);
        assert.equal(await page.locator('button[data-kind=lot]').getAttribute('data-page'),'0');
        await page.locator('[name="Input.Template.Content.Steps[0].Text"]').fill('Inspection');
        await page.click('button[type=submit]'); await page.waitForFunction(()=>navigations.length===1);
        assert.equal(await page.evaluate(()=>navigations[0].destination),'Recurrence');
        await page.click('button[data-kind=asset]');
        await page.waitForFunction(()=>typeof completeChoices==='function');
        await page.evaluate(()=>{window.resgridAdpPageConcealed();window.completeChoices();});
        assert.equal(await page.locator('.work-order-protected option').count(),0);
        assert.deepEqual(errors,[]);
        console.log('PASS: PM field prefixes, weekday mask, paged inventory choices, grant/CSRF propagation, safe labels, dependent resets and expiry concealment.');
    } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
