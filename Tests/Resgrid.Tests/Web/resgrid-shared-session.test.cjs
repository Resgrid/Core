// Core Web on a shared workstation (passkey plan sections 10.5 and 12.5.4; workbook section 12, slice 25). Runs the real page
// scripts (the history guard, the workstation bar and the lock screen) in headless Chrome against a small server that plays
// the part of Resgrid's status and lock routes, with the page clock under the test's control. It shows that:
//   - only real input reports operator activity, at most every 30 seconds across every tab of the site;
//   - the page warns before the idle lock, and "Stay signed in" always reports;
//   - when the server says the session is locked, every tab goes to the lock screen, and back once it is unlocked;
//   - a page brought back with the back button stays hidden until the server confirms the session is unlocked;
//   - a refused request, Lock and End shift take every tab with them.
//   node Tests/Resgrid.Tests/Web/resgrid-shared-session.test.cjs   (or through BrowserScriptTests / npm test)
// See browser-launch.cjs for RESGRID_PLAYWRIGHT_PATH and RESGRID_PLAYWRIGHT_CHANNEL.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const launch = require('./browser-launch.cjs');
const { chromium } = launch.playwright();
const root = path.resolve(__dirname, '../../..');
const scripts = {
    '/js/guard.js': fs.readFileSync(path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/common/shared/resgrid.shared.guard.js'), 'utf8'),
    '/js/session.js': fs.readFileSync(path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/common/shared/resgrid.shared.session.js'), 'utf8'),
    '/js/locked.js': fs.readFileSync(path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/common/shared/resgrid.shared.locked.js'), 'utf8'),
    '/js/jquery.js': fs.readFileSync(path.join(root, 'Web/Resgrid.Web/wwwroot/lib/jquery/dist/jquery.min.js'), 'utf8')
};

// The workstation bar as _SharedSessionBar renders it, on an ordinary page with a link to another page.
function page(name) {
    return `<!DOCTYPE html><html><head><script src="/js/guard.js"></script></head><body>
<div id="rgSharedSession" data-status-url="/SharedSession/Status" data-locked-url="/SharedSession/Locked" data-shift-ended-url="/Account/LogOn?reason=shift_ended">
  <span id="rgSharedCountdown"></span>
  <form id="rgSharedLockForm" method="post" action="/SharedSession/Lock"><input type="hidden" name="returnUrl" value="/${name}"><button id="rgSharedLock">Lock</button></form>
  <form method="post" action="/SharedSession/EndShift"><input type="hidden" name="switchOperator" value="false"><button id="rgSharedEndShift">End shift</button></form>
  <div id="rgSharedWarning" style="display:none"><span id="rgSharedWarningText"></span><button type="button" id="rgSharedStay">Stay</button></div>
</div>
<h1 id="content">${name}</h1><a id="next" href="/page2">next</a>
<script src="/js/jquery.js"></script><script src="/js/session.js"></script>
<script>resgridSharedSession.init({ messages: { locksIn: 'Locks in {0}', idleWarning: 'Locks in {0} seconds', shiftEndsSoon: 'Shift ends in {0} minutes' } });</script>
</body></html>`;
}

// The lock screen as SharedSession/Locked renders it.
function lockedPage(returnUrl) {
    return `<!DOCTYPE html><html><body>
<div id="sharedLocked" data-status-url="/SharedSession/Status" data-return-url="${returnUrl || ''}"><h3>Workstation locked</h3></div>
<form method="post" action="/SharedSession/EndShift" id="endShift"><input type="hidden" name="switchOperator" value="false"><button id="sharedEndShift">End shift</button></form>
<script src="/js/locked.js"></script></body></html>`;
}

const state = { locked: false, ended: false, idle: 300, shift: 43200, delay: 0, lockedDelay: 0 };
const seen = [];

function statusBody() {
    if (state.ended)
        return [401, ''];
    return [200, JSON.stringify({ shared: true, locked: state.locked, lockVersion: 1, idleLockMinutes: 5,
        idleLocksInSeconds: state.locked ? null : state.idle, shiftEndsInSeconds: state.shift })];
}

const server = http.createServer((request, response) => {
    const url = new URL(request.url, 'http://localhost');
    let body = '';
    request.on('data', chunk => body += chunk);
    request.on('end', () => {
        const entry = { method: request.method, path: url.pathname, query: url.search, activity: request.headers['x-resgrid-operator-activity'] === '1', body };
        seen.push(entry);
        if (scripts[url.pathname]) {
            response.writeHead(200, { 'Content-Type': 'text/javascript' }).end(scripts[url.pathname]);
        } else if (url.pathname === '/page1' || url.pathname === '/page2') {
            response.writeHead(200, { 'Content-Type': 'text/html' }).end(page(url.pathname.substring(1)));
        } else if (url.pathname === '/SharedSession/Status') {
            const [code, json] = statusBody();
            setTimeout(() => response.writeHead(code, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' }).end(json), state.delay);
        } else if (url.pathname === '/SharedSession/Locked') {
            setTimeout(() => response.writeHead(200, { 'Content-Type': 'text/html', 'Cache-Control': 'no-store' }).end(lockedPage(url.searchParams.get('returnUrl'))),
                state.lockedDelay);
        } else if (url.pathname === '/SharedSession/Lock') {
            state.locked = true;
            response.writeHead(302, { Location: '/SharedSession/Locked?returnUrl=' + encodeURIComponent(new URLSearchParams(body).get('returnUrl')) }).end();
        } else if (url.pathname === '/SharedSession/EndShift') {
            state.ended = true;
            response.writeHead(302, { Location: '/Account/LogOn' }).end();
        } else if (url.pathname === '/Account/LogOn') {
            response.writeHead(200, { 'Content-Type': 'text/html' }).end('<!DOCTYPE html><h1 id="signin">Sign in</h1>');
        } else if (url.pathname === '/refused') {
            response.writeHead(401, { 'Content-Type': 'application/json' }).end(JSON.stringify({ error: 'shared_session_locked', lock_version: 1 }));
        } else {
            response.writeHead(404).end();
        }
    });
});

const statuses = () => seen.filter(r => r.path === '/SharedSession/Status');
const activities = () => statuses().filter(r => r.activity);

async function eventually(check, message) {
    for (let i = 0; i < 100; i++) {
        if (await check())
            return;
        await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.fail(message);
}

(async () => {
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const base = 'http://localhost:' + server.address().port;
    const browser = await chromium.launch(launch.launchOptions());
    const reset = () => Object.assign(state, { locked: false, ended: false, idle: 300, shift: 43200, delay: 0, lockedDelay: 0 });

    // The page clock is the test's, except where the browser's own navigation timing is needed: Playwright's fake clock
    // replaces performance, which the history guard reads.
    async function station(realClock) {
        const context = await browser.newContext();
        if (!realClock)
            await context.clock.install({ time: new Date('2026-09-29T12:00:00Z') });
        return context;
    }

    async function open(context, where) {
        const tab = await context.newPage();
        const before = statuses().length;
        await tab.goto(base + where);
        await eventually(async () => statuses().length > before && (await tab.textContent('#rgSharedCountdown')) !== '', 'the page asks the server first');
        return tab;
    }

    try {
        // ---- Activity: only real input, at most every 30 seconds across tabs; polling never counts ----
        reset();
        seen.length = 0;
        let context = await station();
        const a = await open(context, '/page1');
        assert.equal(await a.textContent('#rgSharedCountdown'), 'Locks in 5:00');
        assert.equal(activities().length, 0, 'loading a page is not the operator');

        await a.keyboard.press('a');
        await eventually(() => activities().length === 1, 'a key press is reported');
        await a.mouse.click(5, 5);
        await a.keyboard.press('b');
        assert.equal(activities().length, 1, 'at most once per 30 seconds');

        const b = await open(context, '/page1');
        state.idle = 250;
        await a.clock.runFor(31000);
        await a.keyboard.press('c');
        await eventually(() => activities().length === 2, 'reported again after 30 seconds');
        await eventually(async () => (await b.textContent('#rgSharedCountdown')) === 'Locks in 4:10', 'the other tab follows the new deadline');
        await b.keyboard.press('d');
        assert.equal(activities().length, 2, 'the interval is shared by every tab of the site');

        const polls = statuses().length;
        await a.clock.runFor(61000);
        await eventually(() => statuses().length > polls, 'the page polls the server every minute');
        assert.ok(statuses().slice(polls).every(r => !r.activity), 'polling never counts as activity');

        // ---- The warning before the idle lock, and Stay signed in ----
        state.idle = 65;
        await a.clock.runFor(61000);
        await eventually(async () => (await a.textContent('#rgSharedCountdown')).startsWith('Locks in 1:0'), 'the countdown follows the server');
        await a.clock.runFor(6000);
        await eventually(async () => a.isVisible('#rgSharedWarning'), 'the warning shows before the lock');
        assert.match(await a.textContent('#rgSharedWarningText'), /^Locks in \d+ seconds$/);
        // A key press reports activity; the server's answer still leaves the warning up, so Stay signed in is pressed
        // well inside the 30-second interval, and must report anyway.
        state.idle = 50;
        const pressed = activities().length;
        await a.keyboard.press('e');
        await eventually(() => activities().length === pressed + 1, 'the key press is reported');
        await eventually(async () => a.isVisible('#rgSharedWarning'), 'the warning is still up');
        state.idle = 300;
        const stayed = activities().length;
        await a.click('#rgSharedStay');
        await eventually(() => activities().length === stayed + 1, 'Stay signed in reports activity even within the 30-second interval');
        await eventually(async () => !(await a.isVisible('#rgSharedWarning')), 'and the warning goes away');

        // ---- The server locks the session: every tab goes to the lock screen, and back once unlocked ----
        state.idle = 3;
        await a.clock.runFor(61000);
        state.locked = true;
        await a.clock.runFor(5000);
        await a.waitForURL(base + '/SharedSession/Locked?returnUrl=%2Fpage1');
        await b.waitForURL(base + '/SharedSession/Locked?returnUrl=%2Fpage1');

        state.locked = false;
        await a.goto(base + '/page2');          // the unlock sends this tab back; its page tells the others
        await b.waitForURL(base + '/page1');
        await context.close();

        // ---- The back button: hidden until the server confirms, and the lock screen if it is locked ----
        reset();
        context = await station(true);
        const c = await open(context, '/page1');
        await c.click('#next');
        await eventually(async () => (await c.textContent('#content')) === 'page2', 'the next page loads');
        state.locked = true;
        state.delay = 400;
        await c.goBack();
        assert.equal(await c.evaluate(() => document.documentElement.classList.contains('rg-shared-concealed')), true,
            'a page from history is hidden before the server answers');
        assert.equal(await c.evaluate(() => getComputedStyle(document.body).visibility), 'hidden');
        await c.waitForURL(base + '/SharedSession/Locked?returnUrl=%2Fpage1');

        state.locked = false;
        state.delay = 0;
        await c.goto(base + '/page1');
        await c.click('#next');
        await eventually(async () => (await c.textContent('#content')) === 'page2', 'the next page loads');
        await c.goBack();
        await eventually(async () => c.evaluate(() => !document.documentElement.classList.contains('rg-shared-concealed')),
            'an unlocked session shows the page again');
        assert.equal(c.url(), base + '/page1');
        await context.close();

        // ---- A refused request, Lock and End shift take every tab with them ----
        reset();
        context = await station();
        const d = await open(context, '/page1');
        const e = await open(context, '/page2');
        // The page that sees the refusal tells the others at once, before its own lock screen has even loaded.
        state.lockedDelay = 1500;
        const others = e.waitForRequest((r) => r.url().includes('/SharedSession/Locked'), { timeout: 1000 });
        await d.evaluate(() => { $.get('/refused'); });
        await others;
        await d.waitForURL(base + '/SharedSession/Locked?returnUrl=%2Fpage1');
        await e.waitForURL(base + '/SharedSession/Locked?returnUrl=%2Fpage2');
        state.lockedDelay = 0;
        await d.goto(base + '/page1');
        await e.waitForURL(base + '/page2');

        // A tab the server sends to the lock screen (a page load while locked) takes the others with it too.
        await d.goto(base + '/SharedSession/Locked?returnUrl=%2Fpage1');
        await e.waitForURL(base + '/SharedSession/Locked?returnUrl=%2Fpage2');
        await d.goto(base + '/page1');
        await e.waitForURL(base + '/page2');

        await e.click('#rgSharedLock');
        await e.waitForURL(base + '/SharedSession/Locked?returnUrl=%2Fpage2');
        await d.waitForURL(base + '/SharedSession/Locked?returnUrl=%2Fpage1');

        await d.click('#sharedEndShift');
        await d.waitForURL(base + '/Account/LogOn');
        await e.waitForURL(base + '/Account/LogOn');
        await context.close();

        // ---- A session that ended goes to sign-in; one past its shift says so ----
        reset();
        context = await station();
        const f = await open(context, '/page1');
        state.ended = true;
        await f.clock.runFor(61000);
        await f.waitForURL(base + '/Account/LogOn?returnUrl=%2Fpage1');

        reset();
        state.shift = 2;
        const g = await open(context, '/page1');
        state.ended = true;
        await g.clock.runFor(3000);
        await g.waitForURL(base + '/Account/LogOn?reason=shift_ended');
        await context.close();

        console.log('resgrid-shared-session: all checks passed');
    } finally {
        await browser.close();
        server.close();
    }
})().catch(error => {
    console.error(error);
    process.exit(1);
});
