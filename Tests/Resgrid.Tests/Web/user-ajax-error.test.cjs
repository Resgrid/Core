// The layout's global ajaxError handler (resgrid.user.js): where a refused script call sends the page.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname, '../../../Web/Resgrid.Web/wwwroot/js/app/internal/resgrid.user.js'), 'utf8');

function load(pathname) {
    let handler = null;
    const visits = [];
    const $ = () => ({ ajaxError: fn => { handler = fn; }, ready: () => { } });
    $.fn = {};
    const window = { location: { origin: 'https://resgrid.test', pathname, search: '', assign: url => visits.push(url) } };
    vm.runInNewContext(source, { $, window, document: {}, resgrid: { absoluteBaseUrl: 'https://resgrid.test' }, URL, JSON, encodeURIComponent, console });
    assert.ok(handler, 'resgrid.user.js registers an ajaxError handler');
    return { fail: jqxhr => handler({}, jqxhr), visits };
}

// A jQuery xhr; .load() asks for HTML, so a JSON body comes with responseJSON unset.
function xhr(status, { body, header, contentType = 'application/json; charset=utf-8', parsed = false } = {}) {
    const headers = { 'x-resgrid-step-up': header, 'content-type': body === undefined ? undefined : contentType };
    return {
        status,
        responseJSON: parsed ? body : undefined,
        responseText: body === undefined ? '' : JSON.stringify(body),
        getResponseHeader: name => headers[name.toLowerCase()] || null
    };
}

const enrollment = { success: false, error: 'mfa_enrollment_required', redirect: '/User/TwoFactor/Enable2FA?enforced=1' };

// A page left open when enforcement started goes to the setup page, whether the call parsed the body or not.
for (const parsed of [false, true]) {
    const page = load('/User/Home/Dashboard');
    page.fail(xhr(403, { body: enrollment, parsed }));
    assert.deepEqual(page.visits, ['https://resgrid.test/User/TwoFactor/Enable2FA?enforced=1']);
}

// The setup page's own calls get the same answer and must not reload it (each load stages a new authenticator key).
const setup = load('/User/TwoFactor/Enable2FA');
setup.fail(xhr(403, { body: enrollment }));
assert.deepEqual(setup.visits, []);

// Only ever a page on this site.
const elsewhere = load('/User/Home/Dashboard');
elsewhere.fail(xhr(403, { body: { ...enrollment, redirect: 'https://evil.test/User/TwoFactor/Enable2FA' } }));
elsewhere.fail(xhr(403, { body: { ...enrollment, redirect: '//evil.test/x' } }));
assert.deepEqual(elsewhere.visits, []);

// Any other 403 body leaves the page alone; the step-up header and a 401 still work as before.
const other = load('/User/Home/Dashboard');
other.fail(xhr(403, { body: { success: false, error: 'step_up_required' } }));
other.fail(xhr(403, { body: 'not json', contentType: 'text/html' }));
assert.deepEqual(other.visits, []);
other.fail(xhr(403, { header: '/User/TwoFactor/Verify2FA?returnUrl=%2F' }));
other.fail(xhr(401));
assert.deepEqual(other.visits, [
    'https://resgrid.test/User/TwoFactor/Verify2FA?returnUrl=%2F',
    'https://resgrid.test/Account/LogOn?returnUrl=%2FUser%2FHome%2FDashboard'
]);

console.log('user-ajax-error: ok');
