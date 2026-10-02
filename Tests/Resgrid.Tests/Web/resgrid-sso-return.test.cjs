// The return from the department's identity provider on Web (passkey plan section 7.7.2; workbook section 12). The Web's cookie
// policy makes every cookie SameSite=Strict, so the browser's arrival from the provider, a cross-site navigation, carries none of
// them. Account/SsoReturn's page reads nothing and posts the return on from this site: a same-site request that carries them.
// Runs the real view's markup and the real page script in headless Chrome across two sites (resgrid.test and idp.example).
//   node Tests/Resgrid.Tests/Web/resgrid-sso-return.test.cjs   (or through BrowserScriptTests / npm test)
// See browser-launch.cjs for RESGRID_PLAYWRIGHT_PATH and RESGRID_PLAYWRIGHT_CHANNEL.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const launch = require('./browser-launch.cjs');
const { chromium } = launch.playwright();
const root = path.resolve(__dirname, '../../..');
const view = fs.readFileSync(path.join(root, 'Web/Resgrid.Web/Views/Account/SsoReturnContinue.cshtml'), 'utf8');
const script = fs.readFileSync(path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/common/sso/resgrid.sso.return.js'), 'utf8');
const policy = "default-src 'none'; script-src 'self'; base-uri 'none'; frame-ancestors 'none'";

// The view as Razor renders it for one return (it has no logic beyond these substitutions); anything left over is drift.
function render(code, state) {
    const html = view
        .replace(/^@(using|model|inject) .*\r?\n/gm, '')
        .replace(/^@\{[\s\S]*?^\}\r?\n/m, '')
        .replace('@Url.Action("SsoReturn", "Account")', '/Account/SsoReturn')
        .replace(' asp-antiforgery="false"', '')
        .replace(' asp-append-version="true"', '')
        .replace('~/js/', '/js/')
        .replace('@Model.Code', code).replace('@Model.State', state).replace('@Model.Error', '')
        .replace('@localizer["SsoReturnContinuing"]', 'Returning').replace('@localizer["SsoReturnContinue"]', 'Continue');
    assert.ok(!html.includes('@') && !html.includes('asp-') && !html.includes('~/'), 'the test renders all of the view:\n' + html);
    return html;
}

function cookiesOf(request) {
    return Object.fromEntries((request.headers.cookie || '').split(';').map(c => c.trim()).filter(Boolean).map(c => c.split('=')));
}

(async () => {
    const seen = [];
    const server = http.createServer((request, response) => {
        const host = (request.headers.host || '').split(':')[0];
        const url = new URL(request.url, 'http://' + request.headers.host);
        let body = '';
        request.on('data', chunk => body += chunk);
        request.on('end', () => {
            seen.push({ host, method: request.method, path: url.pathname, cookies: cookiesOf(request), body });
            if (host === 'resgrid.test' && url.pathname === '/start') {
                // The round trip, a signed-in session and a Lax control cookie, as this site set them before leaving.
                response.setHeader('Set-Cookie', ['rt=trip; Path=/Account/SsoReturn; HttpOnly; SameSite=Strict',
                    'auth=session; Path=/; HttpOnly; SameSite=Strict', 'lax=control; Path=/; SameSite=Lax']);
                response.end('<!DOCTYPE html><a id="go" href="http://idp.example:' + port + '/authorize">Sign in</a>');
            } else if (host === 'idp.example' && url.pathname === '/authorize') {
                response.end('<!DOCTYPE html><a id="done" href="/callback">Continue</a>');
            } else if (host === 'idp.example' && url.pathname === '/callback') {
                // The provider (by way of the broker) sends the browser back with only a code and the state.
                response.writeHead(302, { Location: 'http://resgrid.test:' + port + '/Account/SsoReturn?sso_code=c1&state=s1' }).end();
            } else if (host === 'resgrid.test' && url.pathname === '/Account/SsoReturn' && request.method === 'GET') {
                response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Content-Security-Policy': policy, 'Cache-Control': 'no-store' });
                response.end(render(url.searchParams.get('sso_code'), url.searchParams.get('state')));
            } else if (host === 'resgrid.test' && url.pathname === '/js/app/common/sso/resgrid.sso.return.js') {
                response.writeHead(200, { 'Content-Type': 'text/javascript' }).end(script);
            } else if (host === 'resgrid.test' && url.pathname === '/Account/SsoReturn' && request.method === 'POST') {
                response.writeHead(302, { Location: '/User/Home' }).end();
            } else if (host === 'resgrid.test' && url.pathname === '/User/Home') {
                response.end('<!DOCTYPE html><p id="home">Home</p>');
            } else {
                response.writeHead(404).end();
            }
        });
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const port = server.address().port;
    const browser = await chromium.launch({
        ...launch.launchOptions(),
        args: ['--host-resolver-rules=MAP resgrid.test 127.0.0.1, MAP idp.example 127.0.0.1']
    });
    const find = (method, pathname) => seen.filter(r => r.host === 'resgrid.test' && r.method === method && r.path === pathname);

    async function signInThroughProvider(contextOptions) {
        seen.length = 0;
        const context = await browser.newContext(contextOptions);
        const page = await context.newPage();
        const refused = [];
        page.on('console', message => { if (/Content Security Policy|Refused/i.test(message.text())) refused.push(message.text()); });
        await page.goto('http://resgrid.test:' + port + '/start');
        await page.click('#go');
        await page.waitForURL('http://idp.example:' + port + '/authorize');
        await page.click('#done');
        return { context, page, refused };
    }

    try {
        // ---- With script: the arrival carries no Strict cookie; the page's own post carries them all ----
        let { context, page, refused } = await signInThroughProvider({});
        await page.waitForURL('http://resgrid.test:' + port + '/User/Home');
        const arrival = find('GET', '/Account/SsoReturn');
        assert.equal(arrival.length, 1);
        assert.equal(arrival[0].cookies.lax, 'control', 'a cross-site top-level navigation carries Lax cookies');
        assert.equal(arrival[0].cookies.rt, undefined, 'the provider\'s return carries no Strict cookie: the round trip is not here');
        assert.equal(arrival[0].cookies.auth, undefined, 'nor the session');
        const posted = find('POST', '/Account/SsoReturn');
        assert.equal(posted.length, 1, 'posted on once');
        assert.equal(posted[0].cookies.rt, 'trip', 'the page\'s own post carries the round trip');
        assert.equal(posted[0].cookies.auth, 'session', 'and the session');
        assert.deepEqual(Object.fromEntries(new URLSearchParams(posted[0].body)), { sso_code: 'c1', state: 's1', error: '' });
        assert.equal(find('GET', '/User/Home')[0].cookies.auth, 'session', 'where the return goes next is same-site too');
        assert.deepEqual(refused, [], 'the page script runs under the page\'s content security policy');
        await context.close();

        // ---- Without script: the page's button posts it on the same way ----
        ({ context, page } = await signInThroughProvider({ javaScriptEnabled: false }));
        await page.waitForURL('http://resgrid.test:' + port + '/Account/SsoReturn?sso_code=c1&state=s1');
        assert.equal(find('POST', '/Account/SsoReturn').length, 0, 'nothing is posted without script until the button is pressed');
        await page.click('button[type=submit]');
        await page.waitForURL('http://resgrid.test:' + port + '/User/Home');
        const pressed = find('POST', '/Account/SsoReturn');
        assert.equal(pressed.length, 1);
        assert.equal(pressed[0].cookies.rt, 'trip');
        assert.equal(pressed[0].cookies.auth, 'session');
        await context.close();

        console.log('resgrid-sso-return: all checks passed');
    } finally {
        await browser.close();
        server.close();
    }
})().catch(error => {
    console.error(error);
    process.exit(1);
});
