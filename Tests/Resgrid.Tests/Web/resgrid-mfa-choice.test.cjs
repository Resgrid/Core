// Second-factor choices on Web sign-in and step-up (passkey workbook section 12, slice 21): a passkey for the web and approval
// from Responder, with the server deciding every outcome and naming where to go. Runs the real page script in headless Chrome
// with jQuery real and the server and browser ceremony stubbed.
//   node Tests/Resgrid.Tests/Web/resgrid-mfa-choice.test.cjs   (or through BrowserScriptTests / npm test)
// See browser-launch.cjs for RESGRID_PLAYWRIGHT_PATH and RESGRID_PLAYWRIGHT_CHANNEL.
const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('./browser-launch.cjs').playwright();
const root = path.resolve(__dirname, '../../..');
const jquery = path.join(root, 'Web/Resgrid.Web/wwwroot/lib/jquery/dist/jquery.min.js');
const script = path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/common/passkeys/resgrid.mfa.choice.js');

const markup = `
<form id="f"><input name="__RequestVerificationToken" value="af"><input id="ReturnUrl" value="/User/Calls"></form>
<button id="mfaUsePasskey">Passkey</button><button id="mfaUseResponder">Responder</button>
<div id="mfaApprovalPanel" style="display:none"><strong id="mfaApprovalNumber"></strong><p id="mfaApprovalStatus"></p><button id="mfaApprovalCancel">Stop</button></div>
<div id="mfaChoiceError" style="display:none"></div>`;

async function harness(page, { supported = true, approval = true } = {}) {
    await page.setContent(markup);
    await page.addScriptTag({ path: jquery });
    await page.evaluate(({ isSupported }) => {
        window.posts = []; window.went = []; window.ceremonies = []; window.intervals = [];
        $.post = function (url, data) { var d = $.Deferred(); window.posts.push({ url: url, data: data, d: d }); return d.promise(); };
        // Polling is driven by the test, not the clock.
        window.setInterval = function (fn) { window.intervals.push(fn); return window.intervals.length; };
        window.clearInterval = function (id) { window.intervals[id - 1] = null; };
        window.resgridPasskeys = {
            isSupported: () => isSupported,
            authenticate: (options) => new Promise((resolve, reject) => window.ceremonies.push({ options, resolve, reject }))
        };
    }, { isSupported: supported });
    await page.addScriptTag({ path: script });
    await page.evaluate((withApproval) => resgridMfaChoice.init({
        antiForgeryToken: () => $('#f input[name="__RequestVerificationToken"]').val(),
        extra: () => ({ returnUrl: $('#ReturnUrl').val() }),
        passkeyOptionsUrl: '/options', verifyPasskeyUrl: '/verify',
        requestApprovalUrl: withApproval ? '/request' : undefined, approvalStatusUrl: '/status', completeApprovalUrl: '/complete',
        navigate: (url) => window.went.push(url),
        messages: {
            failed: 'failed', passkey_failed: 'passkey failed', passkey_cancelled: 'cancelled', passkey_not_supported: 'not supported',
            approval_waiting: 'waiting', approval_denied: 'denied', approval_expired: 'expired', mfa_method_not_allowed: 'not allowed'
        }
    }), approval);
}

const resolvePost = (page, index, response) => page.evaluate(([i, r]) => posts[i].d.resolve(r), [index, response]);
const errorText = (page) => page.evaluate(() => $('#mfaChoiceError').is(':visible') ? $('#mfaChoiceError').text() : null);
const poll = (page) => page.evaluate(() => { var live = intervals.filter(Boolean); live[live.length - 1](); });

(async () => {
    const browser = await chromium.launch(require('./browser-launch.cjs').launchOptions());
    try {
        // ---- A passkey: options from the server, the prompt, the result back, then where the server says ----
        let page = await browser.newPage();
        await harness(page);
        await page.click('#mfaUsePasskey');
        assert.deepEqual(await page.evaluate(() => [posts[0].url, posts[0].data.__RequestVerificationToken, posts[0].data.returnUrl]), ['/options', 'af', '/User/Calls']);
        assert.equal(await page.isDisabled('#mfaUsePasskey'), true, 'one ceremony at a time');
        await page.click('#mfaUseResponder');
        assert.equal(await page.evaluate(() => posts.length), 1, 'nothing else starts while a ceremony runs');
        await resolvePost(page, 0, { success: true, requestId: 'r1', options: '{"challenge":"abc"}' });
        assert.equal(await page.evaluate(() => ceremonies[0].options), '{"challenge":"abc"}');
        await page.evaluate(() => ceremonies[0].resolve({ id: 'cred', type: 'public-key' }));
        assert.deepEqual(await page.evaluate(() => [posts[1].url, posts[1].data.requestId, posts[1].data.credential, posts[1].data.returnUrl]),
            ['/verify', 'r1', '{"id":"cred","type":"public-key"}', '/User/Calls']);
        await resolvePost(page, 1, { success: true, redirect: '/User/Calls' });
        assert.deepEqual(await page.evaluate(() => went), ['/User/Calls']);
        await page.close();

        // ---- Only a local address the server named is followed ----
        page = await browser.newPage();
        await harness(page);
        for (const target of ['https://evil.example/', '//evil.example/', '/\\evil.example']) {
            await page.click('#mfaUsePasskey');
            const n = await page.evaluate(() => posts.length);
            await resolvePost(page, n - 1, { success: true, requestId: 'r', options: '{}' });
            await page.evaluate(() => ceremonies[ceremonies.length - 1].resolve({ id: 'c' }));
            await resolvePost(page, n, { success: true, redirect: target });
            assert.equal(await errorText(page), 'failed', target);
        }
        assert.deepEqual(await page.evaluate(() => went), []);
        await page.close();

        // ---- A closed prompt, a failed passkey, a refused method and an unsupported browser are each told plainly ----
        page = await browser.newPage();
        await harness(page);
        await page.click('#mfaUsePasskey');
        await resolvePost(page, 0, { success: true, requestId: 'r1', options: '{}' });
        await page.evaluate(() => ceremonies[0].reject({ outcome: 'cancelled' }));
        assert.equal(await errorText(page), 'cancelled');
        assert.equal(await page.evaluate(() => posts.length), 1, 'nothing is sent for a closed prompt');
        assert.equal(await page.isDisabled('#mfaUsePasskey'), false);
        await page.click('#mfaUsePasskey');
        await resolvePost(page, 1, { success: false, error: 'mfa_method_not_allowed' });
        assert.equal(await errorText(page), 'not allowed');
        await page.click('#mfaUsePasskey');
        await resolvePost(page, 2, { success: true, requestId: 'r3', options: '{}' });
        await page.evaluate(() => ceremonies[1].resolve({ id: 'c' }));
        await resolvePost(page, 3, { success: false, error: 'passkey_verification_failed' });
        assert.equal(await errorText(page), 'failed');
        await page.close();

        page = await browser.newPage();
        await harness(page, { supported: false });
        await page.click('#mfaUsePasskey');
        assert.equal(await errorText(page), 'not supported');
        assert.equal(await page.evaluate(() => posts.length), 0);
        await page.close();

        // ---- A sign-in that ended goes back to where the server says to start again ----
        page = await browser.newPage();
        await harness(page);
        await page.click('#mfaUsePasskey');
        await resolvePost(page, 0, { success: false, error: 'mfa_transaction_expired', restart: '/Account/LogOn?returnUrl=%2FUser%2FCalls' });
        assert.deepEqual(await page.evaluate(() => went), ['/Account/LogOn?returnUrl=%2FUser%2FCalls']);
        await page.close();

        // ---- Approval: the number on this page, polled until decided, then used once ----
        page = await browser.newPage();
        await harness(page);
        await page.click('#mfaUseResponder');
        assert.equal(await page.evaluate(() => posts[0].url), '/request');
        await resolvePost(page, 0, { success: true, approvalRequestId: 'ap-1', matchNumber: '47', expiresIn: 120 });
        assert.equal(await page.textContent('#mfaApprovalNumber'), '47');
        assert.equal(await page.textContent('#mfaApprovalStatus'), 'waiting');
        assert.equal(await page.isVisible('#mfaApprovalPanel'), true);
        await poll(page);
        assert.deepEqual(await page.evaluate(() => [posts[1].url, posts[1].data.approvalRequestId]), ['/status', 'ap-1']);
        await resolvePost(page, 1, { success: true, state: 'pending' });
        assert.equal(await page.isVisible('#mfaApprovalPanel'), true, 'still waiting');
        await poll(page);
        await resolvePost(page, 2, { success: true, state: 'approved' });
        assert.deepEqual(await page.evaluate(() => [posts[3].url, posts[3].data.approvalRequestId, posts[3].data.returnUrl]), ['/complete', 'ap-1', '/User/Calls']);
        assert.equal(await page.isVisible('#mfaApprovalPanel'), false);
        assert.equal(await page.evaluate(() => intervals.filter(Boolean).length), 0, 'polling stops once decided');
        await resolvePost(page, 3, { success: true, redirect: '/User/Dashboard' });
        assert.deepEqual(await page.evaluate(() => went), ['/User/Dashboard']);
        await page.close();

        // ---- A denied or expired request stops the wait with its reason; stopping the wait asks nothing more ----
        for (const [state, message] of [['denied', 'denied'], ['expired', 'expired'], ['canceled', 'expired']]) {
            page = await browser.newPage();
            await harness(page);
            await page.click('#mfaUseResponder');
            await resolvePost(page, 0, { success: true, approvalRequestId: 'ap-1', matchNumber: '12' });
            await poll(page);
            await resolvePost(page, 1, { success: true, state: state });
            assert.equal(await errorText(page), message, state);
            assert.equal(await page.evaluate(() => posts.length), 2, 'nothing is completed');
            await page.close();
        }

        page = await browser.newPage();
        await harness(page);
        await page.click('#mfaUseResponder');
        await resolvePost(page, 0, { success: true, approvalRequestId: 'ap-1', matchNumber: '12' });
        await page.click('#mfaApprovalCancel');
        assert.equal(await page.isVisible('#mfaApprovalPanel'), false);
        assert.equal(await page.evaluate(() => intervals.filter(Boolean).length), 0);
        await page.click('#mfaUsePasskey');
        assert.equal(await page.evaluate(() => posts[posts.length - 1].url), '/options', 'another method can be chosen right away');
        await page.close();

        // ---- Switching to a passkey while waiting ends the wait, and a late answer for the old request is ignored ----
        page = await browser.newPage();
        await harness(page);
        await page.click('#mfaUseResponder');
        await resolvePost(page, 0, { success: true, approvalRequestId: 'ap-1', matchNumber: '12' });
        await poll(page);
        await page.click('#mfaUsePasskey');
        await resolvePost(page, 1, { success: true, state: 'approved' });
        assert.equal(await page.evaluate(() => posts.filter(p => p.url === '/complete').length), 0, 'the old request is not used');
        await page.close();

        console.log('resgrid-mfa-choice.test.cjs passed');
    } finally {
        await browser.close();
    }
})().catch((error) => { console.error(error); process.exit(1); });
