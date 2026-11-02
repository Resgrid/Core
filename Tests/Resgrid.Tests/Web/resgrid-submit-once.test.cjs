// The submit-once guard on the sign-in, registration, password recovery and invitation forms (resgrid.submit-once.js). A form
// marked data-submit-once is posted once however the user repeats the submit (double-click, click then Enter); a submit the page's
// own validation stops never locks the form; a page restored from the back/forward cache posts again. Runs the real script in
// headless Chrome under the recovery pages' script-src 'self' policy, against a server that answers slowly, like a real sign-in.
//   node Tests/Resgrid.Tests/Web/resgrid-submit-once.test.cjs   (or through BrowserScriptTests / npm test)
// See browser-launch.cjs for RESGRID_PLAYWRIGHT_PATH and RESGRID_PLAYWRIGHT_CHANNEL.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const launch = require('./browser-launch.cjs');
const { chromium } = launch.playwright();
const root = path.resolve(__dirname, '../../..');
const script = fs.readFileSync(path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/public/resgrid.submit-once.js'), 'utf8');
const policy = "default-src 'self'; script-src 'self'; style-src 'self'; form-action 'self'; base-uri 'none'";

// The views put the guard after the page's other scripts; validate.js stands in for jQuery Validate (it cancels an empty submit
// on the form itself), late.js for a page check bound on the document after the guard.
// framed posts into an iframe, so the page stays put and can be looked at while its post is on the way.
function page(guarded, framed) {
    return '<!DOCTYPE html><html><head><title>Form</title></head><body>' +
        (framed ? '<iframe name="sink"></iframe>' : '') +
        '<form id="f" action="/post" method="post"' + (guarded ? ' data-submit-once' : '') + (framed ? ' target="sink"' : '') + '>' +
        '<input id="name" name="name">' +
        '<button id="go" type="submit" name="go" value="1">Sign in</button>' +
        '</form>' +
        '<script src="/js/validate.js"></script>' +
        (guarded ? '<script src="/js/resgrid.submit-once.js"></script>' : '') +
        '<script src="/js/late.js"></script>' +
        '</body></html>';
}

const validate = "document.getElementById('f').addEventListener('submit', function (e) {" +
    " if (!document.getElementById('name').value) { e.preventDefault(); e.stopPropagation(); } });";
const late = "document.addEventListener('submit', function (e) { if (window.cancelNext) { window.cancelNext = false; e.preventDefault(); } });";

(async () => {
    const posts = [];
    const server = http.createServer((request, response) => {
        const url = new URL(request.url, 'http://' + request.headers.host);
        let body = '';
        request.on('data', chunk => body += chunk);
        request.on('end', () => {
            const html = { 'Content-Type': 'text/html; charset=utf-8', 'Content-Security-Policy': policy, 'Cache-Control': 'no-store' };
            if (request.method === 'GET' && (url.pathname === '/guarded' || url.pathname === '/plain' || url.pathname === '/framed')) {
                response.writeHead(200, html).end(page(url.pathname !== '/plain', url.pathname === '/framed'));
            } else if (url.pathname === '/js/resgrid.submit-once.js') {
                response.writeHead(200, { 'Content-Type': 'text/javascript' }).end(script);
            } else if (url.pathname === '/js/validate.js') {
                response.writeHead(200, { 'Content-Type': 'text/javascript' }).end(validate);
            } else if (url.pathname === '/js/late.js') {
                response.writeHead(200, { 'Content-Type': 'text/javascript' }).end(late);
            } else if (request.method === 'POST' && url.pathname === '/post') {
                posts.push(Object.fromEntries(new URLSearchParams(body)));
                // A sign-in that takes a while: long enough for the repeat to land while the first post is on the way.
                setTimeout(() => response.writeHead(200, html).end('<!DOCTYPE html><p id="done">Done</p>'), 700);
            } else {
                response.writeHead(404).end();
            }
        });
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const base = 'http://127.0.0.1:' + server.address().port;
    const browser = await chromium.launch(launch.launchOptions());

    async function open(pathname) {
        posts.length = 0;
        const context = await browser.newContext();
        const tab = await context.newPage();
        const errors = [];
        tab.on('pageerror', error => errors.push(error.message));
        tab.on('console', message => { if (/Content Security Policy|Refused/i.test(message.text())) errors.push(message.text()); });
        await tab.goto(base + pathname);
        return { context, tab, errors };
    }

    const settle = () => new Promise(resolve => setTimeout(resolve, 1200));

    try {
        // A user who presses the button again because the page has not answered yet: the first post is already on the wire.
        // (Raw mouse clicks: Playwright's click() waits out a pending navigation, which a user does not.)
        async function pressTwice(tab) {
            const box = await tab.locator('#go').boundingBox();
            await tab.mouse.click(box.x + 5, box.y + 5);
            await tab.waitForTimeout(200);
            await tab.mouse.click(box.x + 5, box.y + 5);
        }

        // ---- The control: without the guard the second press really does post again ----
        let { context, tab, errors } = await open('/plain');
        await tab.fill('#name', 'pat');
        await pressTwice(tab);
        await settle();
        assert.equal(posts.length, 2, 'the browser sends both posts of an unguarded second press');
        await context.close();

        // ---- With the guard: a second press posts nothing, and the clicked button still names itself ----
        ({ context, tab, errors } = await open('/guarded'));
        await tab.fill('#name', 'pat');
        await pressTwice(tab);
        await tab.waitForSelector('#done');
        await settle();
        assert.equal(posts.length, 1, 'a second press posts nothing');
        assert.deepEqual(posts[0], { name: 'pat', go: '1' }, 'the clicked button still names itself in the post');
        assert.deepEqual(errors, [], 'the guard runs under the page\'s content security policy');
        await context.close();

        // ---- A double-click posts once ----
        ({ context, tab } = await open('/guarded'));
        await tab.fill('#name', 'pat');
        await tab.dblclick('#go');
        await tab.waitForSelector('#done');
        await settle();
        assert.equal(posts.length, 1, 'a double-click posts the form once');
        await context.close();

        // ---- Click, then Enter while the first post is on the way: once; the button shows it is working ----
        ({ context, tab } = await open('/framed'));
        await tab.fill('#name', 'pat');
        await tab.click('#go');
        await tab.waitForFunction(() => document.getElementById('go').disabled);
        const working = await tab.evaluate(() => ({
            busy: document.getElementById('f').getAttribute('aria-busy'),
            spinner: !!document.querySelector('#go [data-submit-once-spinner]')
        }));
        assert.deepEqual(working, { busy: 'true', spinner: true }, 'the form says it is working');
        await tab.focus('#name');
        await tab.keyboard.press('Enter');
        await tab.evaluate(() => document.getElementById('f').requestSubmit());
        await settle();
        assert.equal(posts.length, 1, 'a second submit while the first is on the way is dropped');
        await context.close();

        // ---- A submit the page's validation stops never locks the form ----
        ({ context, tab } = await open('/guarded'));
        await tab.click('#go');
        await tab.waitForTimeout(100);
        assert.equal(await tab.evaluate(() => document.getElementById('go').disabled), false, 'an invalid form stays usable');
        assert.equal(await tab.evaluate(() => document.getElementById('f').hasAttribute('data-submitting')), false);
        assert.equal(posts.length, 0);
        await tab.fill('#name', 'pat');
        await tab.click('#go');
        await tab.waitForSelector('#done');
        assert.equal(posts.length, 1, 'the corrected form posts');
        await context.close();

        // ---- A page check bound after the guard that cancels the submit gives the form back ----
        ({ context, tab } = await open('/guarded'));
        await tab.fill('#name', 'pat');
        await tab.evaluate(() => { window.cancelNext = true; });
        await tab.click('#go');
        await tab.waitForTimeout(100);
        assert.equal(await tab.evaluate(() => document.getElementById('go').disabled), false, 'a cancelled submit leaves the form usable');
        assert.equal(posts.length, 0);
        await tab.click('#go');
        await tab.waitForSelector('#done');
        assert.equal(posts.length, 1);
        await context.close();

        // ---- A page restored from the back/forward cache, locked mid-post, posts again ----
        ({ context, tab } = await open('/guarded'));
        await tab.fill('#name', 'pat');
        const restored = await tab.evaluate(() => {
            // The form as the guard leaves it mid-post, which is how the back/forward cache keeps it.
            const form = document.getElementById('f');
            const go = document.getElementById('go');
            form.setAttribute('data-submitting', 'true');
            go.setAttribute('data-submit-once-disabled', 'true');
            go.disabled = true;
            window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
            return { submitting: form.hasAttribute('data-submitting'), disabled: go.disabled };
        });
        assert.deepEqual(restored, { submitting: false, disabled: false }, 'the restored page is a fresh form');
        await context.close();

        console.log('resgrid-submit-once: all checks passed');
    } finally {
        await browser.close();
        server.close();
    }
})().catch(error => {
    console.error(error);
    process.exit(1);
});
