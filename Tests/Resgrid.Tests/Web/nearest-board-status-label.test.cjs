// The status badge on the New Call / Update Call personnel and unit lists (resgrid.dispatch.nearestboard.js statusLabel): a
// status with its own text colour keeps it; one without (the built-in statuses, mapped from a label class: Standing By is
// #777) gets dark or white text by its background instead of the theme's grey label text, which vanished on mid-grey.
//   node Tests/Resgrid.Tests/Web/nearest-board-status-label.test.cjs   (or through BrowserScriptTests / npm test)
// See browser-launch.cjs for RESGRID_PLAYWRIGHT_PATH and RESGRID_PLAYWRIGHT_CHANNEL.
const assert = require('node:assert/strict');
const path = require('node:path');
const launch = require('./browser-launch.cjs');
const { chromium } = launch.playwright();
const root = path.resolve(__dirname, '../../..');
const jquery = path.join(root, 'Web/Resgrid.Web/wwwroot/lib/jquery/dist/jquery.min.js');
const script = path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/internal/dispatch/resgrid.dispatch.nearestboard.js');

(async () => {
    const browser = await chromium.launch(launch.launchOptions());
    try {
        const page = await browser.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        // The theme's label colours (style.css): grey text on every .label unless a colour is set.
        await page.setContent('<style>.label { background-color: #D1DADE; color: #5E5E5E; }</style><table><tbody id="cells"></tbody></table>');
        await page.addScriptTag({ path: jquery });
        await page.addScriptTag({ path: script });

        // The personnel list's Status column, rendered and measured: [background, text colour].
        const badge = (row) => page.evaluate((r) => {
            var column = resgrid.dispatch.nearestboard.personnelTableOptions('/list').columns[2];
            var cell = $('<td>').html(column.render(r.Status, 'display', r)).appendTo('#cells');
            var label = cell.find('.label')[0];
            var style = getComputedStyle(label);
            return [style.backgroundColor, style.color];
        }, row);

        assert.deepEqual(await badge({ Status: 'Standing By', StatusColor: '#777' }), ['rgb(119, 119, 119)', 'rgb(255, 255, 255)'],
            'the built-in Standing By reads white on its mid-grey');
        assert.deepEqual(await badge({ Status: 'Available', StatusColor: '#d1dade' }), ['rgb(209, 218, 222)', 'rgb(38, 38, 38)'],
            'a light status gets dark text');
        assert.deepEqual(await badge({ Status: 'Responding', StatusColor: '#449d44', StatusTextColor: '#ffff00' }), ['rgb(68, 157, 68)', 'rgb(255, 255, 0)'],
            'a status\'s own text colour wins');
        assert.deepEqual(await badge({ Status: 'On Scene', StatusColor: '#000' }), ['rgb(0, 0, 0)', 'rgb(255, 255, 255)'],
            'a three-digit colour is read too');
        // PR #591: chosen by contrast, not a brightness threshold — white on pure green is about 1.4:1, dark about 11:1.
        assert.deepEqual(await badge({ Status: 'Available', StatusColor: '#00ff00' }), ['rgb(0, 255, 0)', 'rgb(38, 38, 38)'],
            'a bright green status gets dark text');
        // A label class (older built-in statuses) keeps the theme's own colours.
        assert.deepEqual(await badge({ Status: 'Standing By', StatusColor: 'label-default' }), ['rgb(209, 218, 222)', 'rgb(94, 94, 94)']);
        assert.deepEqual(errors, []);

        console.log('nearest-board-status-label: all checks passed');
    } finally {
        await browser.close();
    }
})().catch(error => {
    console.error(error);
    process.exit(1);
});
