// ADP reveal module (plan 7.2 / RMS plan 5.9.3): bound forms, the expiry warning and in-place
// re-verification. Runs the real module in headless Edge with a fake clock; jQuery is real,
// Bootstrap's modal and the server endpoints are stubbed.
//   node Tests/Resgrid.Tests/Web/resgrid-adp-reveal.test.cjs   (or through BrowserScriptTests / npm test)
// See browser-launch.cjs for RESGRID_PLAYWRIGHT_PATH and RESGRID_PLAYWRIGHT_CHANNEL.
const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('./browser-launch.cjs').playwright();
const root = path.resolve(__dirname, '../../..');
const jquery = path.join(root, 'Web/Resgrid.Web/wwwroot/lib/jquery/dist/jquery.min.js');
const module_ = path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/internal/dataprotection/resgrid.adp.reveal.js');
const MINUTE = 60 * 1000;

const chrome = `
<div id="adpProtectedBanner"><button id="adpRevealButton">Reveal</button><button id="adpConcealButton">Conceal</button></div>
<div id="adpStepUpModal"><div id="adpStepUpError"></div><input id="adpStepUpCode"><button id="adpStepUpSubmit">Verify</button></div>`;

async function harness(page, html, options) {
    await page.clock.install({ time: new Date('2026-09-05T12:00:00Z') });
    await page.setContent(chrome + html);
    await page.addScriptTag({ path: jquery });
    await page.evaluate(() => {
        window.posts = []; window.ajaxCalls = []; window.modalCalls = []; window.submits = []; window.natural = [];
        window.renewed = 0; window.cancelled = 0;
        $.fn.modal = function (action) { window.modalCalls.push(action); if (action === 'hide') this.trigger('hidden.bs.modal'); return this; };
        $.post = function (url, data) { var d = $.Deferred(); window.posts.push({ url: url, data: data, d: d }); return d.promise(); };
        $.ajax = function (options) { var d = $.Deferred(); window.ajaxCalls.push({ options: options, d: d }); return d.promise(); };
        // The module re-dispatches a held submit through requestSubmit; the stub dispatches a real
        // SubmitEvent so every listener (the module's pass-through included) sees it, and records the outcome.
        HTMLFormElement.prototype.requestSubmit = function (submitter) {
            // The harness's own guard listener cancels every submit (a real one would navigate), so the
            // module's decision is read from window.natural, recorded before that guard runs.
            this.dispatchEvent(new SubmitEvent('submit', { submitter: submitter || null, bubbles: true, cancelable: true }));
            window.submits.push({ id: this.id, submitter: submitter ? submitter.name : null });
        };
    });
    await page.addScriptTag({ path: module_ });
    await page.evaluate((options) => {
        resgridAdpReveal.init(Object.assign({
            verifyUrl: '/verify', requestGrantUrl: '/request', revealUrl: '/reveal', revealData: { callId: '1' },
            antiForgeryToken: 'af', messages: {}, warnBeforeSeconds: 120
        }, options));
        document.querySelectorAll('form').forEach(function (form) {
            // Registered after the module so it sees the module's decision; a natural submit would navigate the page.
            form.addEventListener('submit', function (e) { window.natural.push(!e.defaultPrevented); e.preventDefault(); });
            form.addEventListener('adp:grant-renewed', function () { window.renewed++; });
            form.addEventListener('adp:submit-cancelled', function () { window.cancelled++; });
        });
    }, options);
}

const resolvePost = (page, index, response) => page.evaluate(([i, r]) => posts[i].d.resolve(r), [index, response]);
const field = (page, name) => page.evaluate((n) => { var i = document.querySelector('input[name="' + n + '"]'); return i ? i.value : null; }, name);
const warningText = (page) => page.evaluate(() => { var el = document.getElementById('adpExpiryWarning'); return el && el.style.display !== 'none' ? el.textContent : null; });
const expiresIn = (page, minutes) => page.evaluate((m) => new Date(Date.now() + m * 60000).toISOString(), minutes);

(async () => {
    const browser = await chromium.launch(require('./browser-launch.cjs').launchOptions());
    try {
        // ---- Bound form: held submit, step-up in place, same submit re-dispatched with the grant ----
        let page = await browser.newPage();
        await harness(page, `<form id="f"><input name="Note" value="typed work"><button id="go" type="submit" name="Go" value="1">Save</button></form>`, { bindForms: ['#f'] });
        await page.click('#go');
        assert.deepEqual(await page.evaluate(() => natural), [false], 'a submit without a grant is held');
        assert.equal(await page.evaluate(() => posts[0].url), '/request', 'the exempt-app grant is asked for first');
        await resolvePost(page, 0, { success: false, error: 'step_up_required' });
        assert.deepEqual(await page.evaluate(() => modalCalls), ['show'], 'the prompt opens when no exemption applies');
        await page.fill('#adpStepUpCode', '123456');
        await page.click('#adpStepUpSubmit');
        assert.equal(await page.evaluate(() => posts[1].url), '/verify');
        await resolvePost(page, 1, { success: true, grantToken: 'T1', expiresOnUtc: await expiresIn(page, 15) });
        assert.equal(await field(page, '__ResgridProtectedGrant'), 'T1', 'the grant is written into the form');
        assert.match(await field(page, '__ResgridProtectedGrantExpiresOn'), /^2026-09-05T12:15:00/);
        assert.deepEqual(await page.evaluate(() => submits), [{ id: 'f', submitter: 'Go' }], 'the same submit, same button, is re-dispatched once');
        assert.deepEqual(await page.evaluate(() => natural), [false, true], 'the re-dispatched submit passes through with the grant');
        assert.equal(await field(page, 'Note'), 'typed work');
        assert.equal(await page.evaluate(() => renewed), 1);
        assert.equal(await page.evaluate(() => modalCalls.slice(-1)[0]), 'hide');
        assert.equal(await page.evaluate(() => cancelled), 0, 'a completed prompt is not a cancellation');
        assert.equal(await page.evaluate(() => resgridAdpReveal.hasLiveGrant()), true);

        // ---- Expiry warning with countdown; renewal in place keeps the grant and the form ----
        assert.equal(await warningText(page), null, 'no warning long before expiry');
        await page.clock.fastForward(13 * MINUTE);
        assert.match(await warningText(page) || '', /expires in 2:00/);
        await page.clock.fastForward(30 * 1000);
        assert.match(await warningText(page) || '', /expires in 1:30/);
        await page.click('#adpRenewButton');
        assert.equal(await page.evaluate(() => posts[2].url), '/request');
        await resolvePost(page, 2, { success: true, grantToken: 'T2', expiresOnUtc: await expiresIn(page, 15) });
        assert.equal(await warningText(page), null, 'renewal hides the warning');
        assert.equal(await field(page, '__ResgridProtectedGrant'), 'T2', 'bound forms carry the renewed grant');
        assert.equal(await page.evaluate(() => renewed), 2);
        assert.equal(await page.evaluate(() => modalCalls.filter(c => c === 'show').length), 1, 'an exempt renewal needs no prompt');
        await page.click('#go');
        assert.deepEqual(await page.evaluate(() => natural.slice(-1)), [true], 'a live grant lets the submit through directly');

        // ---- Expiry without renewal: field cleared, typed work kept, expired warning offered ----
        await page.clock.fastForward(16 * MINUTE);
        assert.match(await warningText(page) || '', /has expired/);
        assert.equal(await field(page, '__ResgridProtectedGrant'), '', 'an expired token is not left in the form');
        assert.equal(await field(page, 'Note'), 'typed work', 'expiry never touches typed work');
        assert.equal(await page.evaluate(() => resgridAdpReveal.hasLiveGrant()), false);

        // ---- Cancelling the prompt leaves the form alone and tells the host ----
        await page.click('#go');
        assert.deepEqual(await page.evaluate(() => natural.slice(-1)), [false]);
        await resolvePost(page, 3, { success: false, error: 'step_up_required' });
        await page.evaluate(() => $('#adpStepUpModal').modal('hide'));
        assert.equal(await page.evaluate(() => cancelled), 1);
        assert.equal(await page.evaluate(() => submits.length), 1, 'nothing was re-dispatched');

        // ---- The host reports a refused save (autosave): expired warning without waiting for the clock ----
        await page.evaluate(() => document.getElementById('f').dispatchEvent(new CustomEvent('adp:grant-required')));
        assert.match(await warningText(page) || '', /has expired/);
        await page.close();

        // ---- Server-held grant (a *Revealed page): live submits pass, warning runs, expiry clears ----
        page = await browser.newPage();
        await harness(page, `<form id="f"><input type="hidden" name="__ResgridProtectedGrant" value="S1"><input name="Note" value="draft"><button id="go" type="submit">Save</button></form>`,
            { revealUrl: null, bindForms: ['#f'], grantExpiresOnUtc: new Date(Date.parse('2026-09-05T12:00:00Z') + 5 * MINUTE).toISOString() });
        assert.equal(await page.evaluate(() => resgridAdpReveal.hasLiveGrant()), true, 'the page trusts the expiry it was rendered with');
        await page.click('#go');
        assert.deepEqual(await page.evaluate(() => natural.slice(-1)), [true]);
        assert.equal(await field(page, '__ResgridProtectedGrant'), 'S1', 'the server-issued token is kept as rendered');
        await page.clock.fastForward(3 * MINUTE + 1000);
        assert.match(await warningText(page) || '', /expires in 1:59/);
        await page.clock.fastForward(2 * MINUTE);
        assert.match(await warningText(page) || '', /has expired/);
        assert.equal(await field(page, '__ResgridProtectedGrant'), '');
        await page.click('#go');
        assert.deepEqual(await page.evaluate(() => natural.slice(-1)), [false], 'after expiry the submit is held again');
        await page.close();

        // ---- Reveal-only page: values conceal at expiry, warning offered before, hidden after ----
        page = await browser.newPage();
        await harness(page, `<input id="notes" data-adp-field="calls.notes:1" value="REDACTED">`, {});
        await page.click('#adpRevealButton');
        await resolvePost(page, 0, { success: true, grantToken: 'R1', expiresOnUtc: await expiresIn(page, 10) });
        assert.equal(await page.evaluate(() => ajaxCalls[0].options.url), '/reveal');
        assert.equal(await page.evaluate(() => ajaxCalls[0].options.headers['X-Resgrid-Protected-Grant']), 'R1');
        await page.evaluate(() => ajaxCalls[0].d.resolve({ success: true, fields: { 'calls.notes:1': 'secret' } }));
        assert.equal(await page.inputValue('#notes'), 'secret');
        await page.clock.fastForward(9 * MINUTE);
        assert.match(await warningText(page) || '', /expires in 1:00/);
        await page.clock.fastForward(2 * MINUTE);
        assert.equal(await page.inputValue('#notes'), 'REDACTED', 'revealed values leave the DOM at expiry');
        assert.equal(await warningText(page), null, 'a page without a form has nothing to hold; the banner takes over');
        await page.close();

        console.log('resgrid-adp-reveal.test.cjs passed');
    } finally {
        await browser.close();
    }
})().catch((error) => { console.error(error); process.exit(1); });
