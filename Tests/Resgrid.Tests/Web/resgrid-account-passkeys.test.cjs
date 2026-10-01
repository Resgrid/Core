// Passkeys on the Web account security page (passkey workbook section 12, slice 19): add, rename and remove through the
// server, following the server when it says to confirm the password or verify again first. Runs the real page script in
// headless Chrome with jQuery real and the server, the browser ceremony, confirm and prompt stubbed.
//   node Tests/Resgrid.Tests/Web/resgrid-account-passkeys.test.cjs   (or through BrowserScriptTests / npm test)
// See browser-launch.cjs for RESGRID_PLAYWRIGHT_PATH and RESGRID_PLAYWRIGHT_CHANNEL.
const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('./browser-launch.cjs').playwright();
const root = path.resolve(__dirname, '../../..');
const jquery = path.join(root, 'Web/Resgrid.Web/wwwroot/lib/jquery/dist/jquery.min.js');
const script = path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/internal/security/resgrid.account.passkeys.js');

const page_ = `
<form id="passkeyAntiForgery"><input name="__RequestVerificationToken" value="af"></form>
<div id="passkeyError" style="display:none"></div><div id="passkeyStatus" style="display:none"></div>
<table><tbody>
  <tr data-passkey-id="pk-web"><td><span class="passkey-name">Laptop</span></td><td><button class="passkey-rename">Rename</button><button class="passkey-remove">Remove</button></td></tr>
</tbody></table>
<input id="passkeyName" value="  Desk key  "><button id="passkeyAdd">Add</button>`;

async function harness(page, supported = true) {
    await page.setContent(page_);
    await page.addScriptTag({ path: jquery });
    await page.evaluate((isSupported) => {
        window.posts = []; window.went = []; window.ceremonies = []; window.promptAnswer = null; window.confirmAnswer = true;
        $.post = function (url, data) { var d = $.Deferred(); window.posts.push({ url: url, data: data, d: d }); return d.promise(); };
        window.prompt = function () { return window.promptAnswer; };
        window.confirm = function () { return window.confirmAnswer; };
        window.resgridPasskeys = {
            isSupported: () => isSupported,
            register: (options) => new Promise((resolve, reject) => window.ceremonies.push({ options, resolve, reject }))
        };
    }, supported);
    await page.addScriptTag({ path: script });
    await page.evaluate(() => resgridAccountPasskeys.init({
        optionsUrl: '/options', completeUrl: '/complete', renameUrl: '/rename', removeUrl: '/remove', pageUrl: '/User/TwoFactor', signInUrl: '/Account/LogOn',
        navigate: (url) => window.went.push(url),
        messages: { failed: 'failed', cancelled: 'cancelled', notSupported: 'not supported', alreadyRegistered: 'already', limit: 'limit',
            unavailable: 'unavailable', renamePrompt: 'New name', removeConfirm: 'Remove?' }
    }));
}

const resolvePost = (page, index, response) => page.evaluate(([i, r]) => posts[i].d.resolve(r), [index, response]);
const errorText = (page) => page.evaluate(() => $('#passkeyError').is(':visible') ? $('#passkeyError').text() : null);

(async () => {
    const browser = await chromium.launch(require('./browser-launch.cjs').launchOptions());
    try {
        // ---- Adding: options from the server, the browser ceremony, the result back, then the page reloads ----
        let page = await browser.newPage();
        await harness(page);
        await page.click('#passkeyAdd');
        assert.deepEqual(await page.evaluate(() => [posts[0].url, posts[0].data.__RequestVerificationToken]), ['/options', 'af']);
        assert.equal(await page.isDisabled('#passkeyAdd'), true, 'one ceremony at a time');
        await resolvePost(page, 0, { success: true, requestId: 'r1', options: '{"challenge":"abc"}' });
        assert.equal(await page.evaluate(() => ceremonies[0].options), '{"challenge":"abc"}');
        await page.evaluate(() => ceremonies[0].resolve({ id: 'new', type: 'public-key' }));
        assert.deepEqual(await page.evaluate(() => [posts[1].url, posts[1].data.requestId, posts[1].data.credential, posts[1].data.displayName]),
            ['/complete', 'r1', '{"id":"new","type":"public-key"}', 'Desk key']);
        await resolvePost(page, 1, { success: true });
        assert.deepEqual(await page.evaluate(() => went), ['/User/TwoFactor?passkeyStatus=added#passkeys']);
        await page.close();

        // ---- The server says who you are must be confirmed first: the page goes where it says ----
        page = await browser.newPage();
        await harness(page);
        await page.click('#passkeyAdd');
        await resolvePost(page, 0, { success: false, error: 'reauthentication_required', redirect: '/User/AccountSecurity/Reauthenticate?returnUrl=%2FUser%2FTwoFactor' });
        assert.deepEqual(await page.evaluate(() => went), ['/User/AccountSecurity/Reauthenticate?returnUrl=%2FUser%2FTwoFactor']);
        assert.equal(await page.evaluate(() => ceremonies.length), 0, 'no prompt before the server agrees');
        await page.close();

        // ---- A closed prompt, a duplicate, the limit, and an unsupported browser are each told plainly ----
        page = await browser.newPage();
        await harness(page);
        await page.click('#passkeyAdd');
        await resolvePost(page, 0, { success: true, requestId: 'r1', options: '{}' });
        await page.evaluate(() => ceremonies[0].reject({ outcome: 'cancelled' }));
        assert.equal(await errorText(page), 'cancelled');
        assert.equal(await page.evaluate(() => posts.length), 1, 'nothing is sent for a cancelled prompt');
        assert.equal(await page.isDisabled('#passkeyAdd'), false);
        await page.click('#passkeyAdd');
        await resolvePost(page, 1, { success: true, requestId: 'r2', options: '{}' });
        await page.evaluate(() => ceremonies[1].reject({ outcome: 'already_registered' }));
        assert.equal(await errorText(page), 'already');
        await page.click('#passkeyAdd');
        await resolvePost(page, 2, { success: false, error: 'passkey_limit_reached' });
        assert.equal(await errorText(page), 'limit');
        await page.close();

        page = await browser.newPage();
        await harness(page, false);
        await page.click('#passkeyAdd');
        assert.equal(await errorText(page), 'not supported');
        assert.equal(await page.evaluate(() => posts.length), 0);
        await page.close();

        // ---- Rename any passkey; an unchanged name sends nothing ----
        page = await browser.newPage();
        await harness(page);
        await page.evaluate(() => { window.promptAnswer = 'Laptop'; });
        await page.click('.passkey-rename');
        assert.equal(await page.evaluate(() => posts.length), 0, 'an unchanged name sends nothing');
        await page.evaluate(() => { window.promptAnswer = '  Work laptop  '; });
        await page.click('.passkey-rename');
        assert.deepEqual(await page.evaluate(() => [posts[0].url, posts[0].data.id, posts[0].data.displayName]), ['/rename', 'pk-web', 'Work laptop']);
        await resolvePost(page, 0, { success: true });
        assert.deepEqual(await page.evaluate(() => went), ['/User/TwoFactor?passkeyStatus=renamed#passkeys']);
        await page.close();

        // ---- Remove: confirmed first, verify again when the server asks, and sign out when it was this session's passkey ----
        page = await browser.newPage();
        await harness(page);
        await page.evaluate(() => { window.confirmAnswer = false; });
        await page.click('.passkey-remove');
        assert.equal(await page.evaluate(() => posts.length), 0, 'nothing is removed without confirmation');
        await page.evaluate(() => { window.confirmAnswer = true; });
        await page.click('.passkey-remove');
        assert.deepEqual(await page.evaluate(() => [posts[0].url, posts[0].data.id]), ['/remove', 'pk-web']);
        await resolvePost(page, 0, { success: false, error: 'step_up_required', redirect: '/User/TwoFactor/Verify2FA?returnUrl=%2FUser%2FTwoFactor' });
        assert.deepEqual(await page.evaluate(() => went), ['/User/TwoFactor/Verify2FA?returnUrl=%2FUser%2FTwoFactor']);
        await page.close();

        page = await browser.newPage();
        await harness(page);
        await page.click('.passkey-remove');
        await resolvePost(page, 0, { success: true, signedOut: true });
        assert.deepEqual(await page.evaluate(() => went), ['/Account/LogOn'], 'removing the passkey this session signed in with ends the session');
        await page.close();

        page = await browser.newPage();
        await harness(page);
        await page.click('.passkey-remove');
        await resolvePost(page, 0, { success: true, signedOut: false });
        assert.deepEqual(await page.evaluate(() => went), ['/User/TwoFactor?passkeyStatus=removed#passkeys']);
        await page.close();

        console.log('resgrid-account-passkeys.test.cjs passed');
    } finally {
        await browser.close();
    }
})().catch((error) => { console.error(error); process.exit(1); });
