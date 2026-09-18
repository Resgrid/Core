// Sidebar module list (_Navigation.cshtml + metisMenu, initialised in site.js).
//
// Records owns a dozen screens, so its links sit in a collapsible group instead of a dozen
// top level entries. These cases pin the behaviour the markup depends on: the group is open
// when the user is already on a records page, the header toggles it, and following a child
// link is not treated as a toggle.
const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('./browser-launch.cjs').playwright();

const root = path.resolve(__dirname, '../../..');
const wwwroot = path.join(root, 'Web/Resgrid.Web/wwwroot');

// A trimmed copy of the sidebar: two flat modules either side of the Records group. The
// classes are exactly what the Razor partial emits.
function menu(recordsOpen) {
    return `
<ul class="nav" id="side-menu">
    <li id="flat-home"><a href="/User/Home/Dashboard"><i class="fa fa-home"></i> <span class="nav-label">Home</span></a></li>
    <li id="records" class="${recordsOpen ? 'active mm-active' : ''}">
        <a href="#" id="records-header" aria-expanded="${recordsOpen ? 'true' : 'false'}">
            <i class="fa fa-file"></i>
            <span class="nav-label">Records</span>
            <span class="fa arrow"></span>
        </a>
        <ul class="nav nav-second-level" id="records-menu">
            <li id="records-all" class="${recordsOpen ? 'active' : ''}"><a href="/User/Records">All records</a></li>
            <li id="records-holds"><a href="/User/RecordLegalHolds">Records preservation holds</a></li>
            <li id="records-analytics"><a href="/User/RecordsAnalytics">Analytics</a></li>
        </ul>
    </li>
    <li id="flat-inventory"><a href="/User/Inventory"><i class="fa fa-barcode"></i> <span class="nav-label">Inventory</span></a></li>
</ul>`;
}

async function build(browser, recordsOpen) {
    const page = await browser.newPage();
    await page.setContent(menu(recordsOpen));
    await page.addScriptTag({ path: path.join(wwwroot, 'lib/jquery/dist/jquery.min.js') });
    await page.addScriptTag({ path: path.join(wwwroot, 'lib/metisMenu/dist/metisMenu.min.js') });
    await page.addStyleTag({ path: path.join(wwwroot, 'lib/metisMenu/dist/metisMenu.min.css') });
    // The same call site.js makes on every page.
    await page.evaluate(() => window.jQuery('#side-menu').metisMenu());
    return page;
}

const visible = (page, selector) => page.evaluate((s) => {
    const element = document.querySelector(s);
    return !!element && element.getBoundingClientRect().height > 0;
}, selector);

(async () => {
    const browser = await chromium.launch(require('./browser-launch.cjs').launchOptions());
    try {
        // A records page: the group is open, and its links are reachable without a click.
        let page = await build(browser, true);
        assert.equal(await page.$eval('#records-menu', (el) => el.classList.contains('mm-show')), true,
            'The Records group did not open on a records page.');
        assert.equal(await visible(page, '#records-holds'), true, 'Records children were hidden while the group was open.');
        assert.equal(await page.$eval('#records-header', (el) => el.getAttribute('aria-expanded')), 'true',
            'The open group did not report itself as expanded.');

        // Collapsing hides the children and flips the arrow state.
        await page.click('#records-header');
        await page.waitForFunction(() => { const menu = document.querySelector('#records-menu'); return menu.classList.contains('mm-collapse') && !menu.classList.contains('mm-collapsing') && !menu.classList.contains('mm-show'); });
        assert.equal(await visible(page, '#records-holds'), false, 'Collapsing the group left its children on screen.');
        assert.equal(await page.$eval('#records', (el) => el.classList.contains('mm-active')), false,
            'The collapsed group still reported itself as expanded.');
        assert.equal(await page.$eval('#records', (el) => el.classList.contains('active')), true,
            'Collapsing the group dropped the highlight for the page the user is on.');

        // And expanding again brings them back.
        await page.click('#records-header');
        await page.waitForFunction(() => document.querySelector('#records-menu').classList.contains('mm-show') && document.querySelector('#records-menu').getBoundingClientRect().height > 0);
        assert.equal(await visible(page, '#records-holds'), true, 'Re-expanding the group did not restore its children.');
        await page.close();

        // Anywhere else: the group is closed, so the sidebar shows one Records entry, not a dozen.
        page = await build(browser, false);
        assert.equal(await visible(page, '#records-holds'), false, 'The Records group was open on an unrelated page.');
        assert.equal(await visible(page, '#flat-home'), true, 'The flat modules disappeared.');
        assert.equal(await visible(page, '#flat-inventory'), true, 'The flat modules disappeared.');

        await page.click('#records-header');
        await page.waitForFunction(() => document.querySelector('#records-menu').classList.contains('mm-show') && document.querySelector('#records-menu').getBoundingClientRect().height > 0);
        assert.equal(await visible(page, '#records-analytics'), true, 'Opening the group did not reveal its children.');

        // A child link navigates; it must not be swallowed as a toggle.
        const target = await page.$eval('#records-analytics a', (el) => el.getAttribute('href'));
        assert.equal(target, '/User/RecordsAnalytics');
        await page.evaluate(() => {
            window.__navigated = null;
            document.querySelector('#records-analytics a').addEventListener('click', (event) => {
                window.__navigated = { href: event.currentTarget.getAttribute('href'), prevented: event.defaultPrevented };
                event.preventDefault();
            });
        });
        await page.click('#records-analytics a');
        const followed = await page.evaluate(() => window.__navigated);
        assert.deepEqual(followed, { href: '/User/RecordsAnalytics', prevented: false },
            'A child link was cancelled instead of navigating.');
        assert.equal(await page.$eval('#records-menu', (el) => el.classList.contains('mm-show')), true,
            'Following a child link collapsed the group.');
        await page.close();

        console.log('Sidebar Records group expand, collapse and child navigation passed.');
    } finally {
        await browser.close();
    }
})().catch((error) => { console.error(error); process.exit(1); });
