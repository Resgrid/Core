const assert = require('node:assert/strict');
const path = require('node:path');
const launch = require('./browser-launch.cjs');
const { chromium } = launch.playwright();
(async () => {
    const browser = await chromium.launch(launch.launchOptions());
    try {
        const page = await browser.newPage(); const errors=[];
        page.on('pageerror',e=>errors.push(e.message));
        await page.setContent(`<p class="work-order-error" hidden></p><div class="work-order-protected">
<form action="https://example.invalid/Save" class="work-order-form work-order-command">
<input name="__RequestVerificationToken" value="synthetic-csrf"><textarea name="Input.Content.Title">private draft</textarea>
<button type="submit">Save</button></form><div id="work-order-steps"></div><button id="work-order-add-step">+</button></div>
<script type="application/json" id="work-order-page">{"protectedData":true,"grant":"synthetic-grant","expiry":"2030-01-01T00:00:00Z","reopen":"/User/WorkOrders/Reopen","index":"/User/WorkOrders","error":"Erreur"}</script>`);
        await page.evaluate(()=>{
            window.$=f=>f();window.requests=[];window.reopened=[];
            window.fail=true;window.allowed=false;
            window.resgridAdpReveal={bindForm:form=>form.addEventListener('submit',e=>{if(!window.allowed)e.preventDefault();})};
            window.fetch=async(url,options)=>{window.requests.push(Object.fromEntries(options.body));return window.fail ? {ok:false,json:async()=>({message:window.errorMessage || 'Conflit',code:'Conflict'})}:{ok:true,json:async()=>({id:123})};};
            HTMLFormElement.prototype.submit=function(){window.reopened.push(Object.fromEntries(new FormData(this)));};
        });
        await page.addScriptTag({path:path.resolve(__dirname,'../../../Web/Resgrid.Web/wwwroot/js/app/internal/workorders/work-orders.js')});
        await page.click('button[type=submit]');
        assert.equal(await page.evaluate(()=>requests.length),0,'ADP verification must run before submission');
        await page.evaluate(()=>window.allowed=true);
        await page.click('button[type=submit]'); await page.waitForFunction(()=>requests.length===1);
        assert.equal(await page.locator('textarea').inputValue(),'private draft');
        assert.equal(await page.locator('.work-order-error').textContent(),'Conflit');
        assert.equal(await page.evaluate(()=>requests[0].__ResgridProtectedGrant),'synthetic-grant');
        await page.evaluate(()=>window.errorMessage='<img src=x onerror="window.xss=true"><script>window.xss=true</script>');
        await page.click('button[type=submit]');
        assert.equal(await page.locator('.work-order-error img, .work-order-error script').count(),0);
        assert.equal(await page.evaluate(()=>window.xss),undefined);
        assert.match(await page.locator('.work-order-error').textContent(),/^<img/);
        await page.evaluate(()=>window.fail=false); await page.click('button[type=submit]');
        await page.waitForFunction(()=>reopened.length===1);
        assert.equal(await page.evaluate(()=>reopened[0].destination),'Detail');
        assert.equal(await page.evaluate(()=>reopened[0].__RequestVerificationToken),'synthetic-csrf');
        assert.equal(await page.evaluate(()=>reopened[0].__ResgridProtectedGrant),'synthetic-grant');
        assert.equal(await page.evaluate(()=>document.getElementById('work-order-page').textContent),'');
        await page.click('#work-order-add-step');
        assert.equal(await page.locator('input[name="Input.Content.Steps[0].Text"]').count(),1);
        assert.deepEqual(errors,[]);
        console.log('PASS: ADP interception, CSRF/grant propagation, conflict preservation, protected POST navigation and step creation.');
    } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
