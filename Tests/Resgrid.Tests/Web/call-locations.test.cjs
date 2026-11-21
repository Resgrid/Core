// The call forms' start location, free destination and route pickers (resgrid.dispatch.calllocations.js, M0322): a destination
// lookup that answers after the dispatcher changed or cleared the destination is ignored; a save with an unresolved plus code waits
// for its lookup (found: the point takes its place and the form goes on with the pressed button; not found: the form stays); a
// route preview still on its way is dropped when a route that can no longer be followed is picked; a location POI with an address
// but no point clears the old pin's coordinates and looks the address up. jQuery is real; the server is stubbed.
//   node Tests/Resgrid.Tests/Web/call-locations.test.cjs   (or through BrowserScriptTests / npm test)
// See browser-launch.cjs for RESGRID_PLAYWRIGHT_PATH and RESGRID_PLAYWRIGHT_CHANNEL.
const assert = require('node:assert/strict');
const path = require('node:path');
const launch = require('./browser-launch.cjs');
const { chromium } = launch.playwright();
const root = path.resolve(__dirname, '../../..');
const jquery = path.join(root, 'Web/Resgrid.Web/wwwroot/lib/jquery/dist/jquery.min.js');
const script = path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/internal/dispatch/resgrid.dispatch.calllocations.js');

// The Update Call form's location POI, address, destination (_CallDestinationLocation) and route (_CallRoutePlanSelect) inputs.
const form = `
<form id="updateCallForm" method="post" action="/User/Dispatch/UpdateCall">
  <select id="Call_LocationPoiId" name="Call.LocationPoiId">
    <option value=""></option>
    <option value="5" data-lat="39.74" data-lng="-104.99" data-address="1 Mercy Way">Mercy</option>
    <option value="6" data-address="9 Clinic Rd">Clinic (no point)</option>
  </select>
  <input id="Call_Address" name="Call.Address" value="1 Main St">
  <a id="searchButton" href="#">Find</a>
  <input id="Latitude" name="Latitude" value="39.7392">
  <input id="Longitude" name="Longitude" value="-104.9903">
  <select id="Call_DestinationPoiId" name="Call.DestinationPoiId"><option value=""></option><option value="31">UZ Gent</option></select>
  <input class="call-destination-free" id="Call_DestinationAddress" name="Call.DestinationAddress">
  <a id="destinationSearchButton" href="#">Find</a>
  <input class="call-destination-free" id="DestinationPlusCode" name="DestinationPlusCode">
  <a id="destinationPlusCodeButton" href="#">Find</a>
  <input class="call-destination-free" id="DestinationLatitude" name="DestinationLatitude">
  <input class="call-destination-free" id="DestinationLongitude" name="DestinationLongitude">
  <input class="call-destination-free" id="Call_DestinationW3W" name="Call.DestinationW3W">
  <a id="destinationW3wButton" href="#">Find</a>
  <a id="destinationClearButton" href="#">Clear</a>
  <select id="Call_RoutePlanId" name="Call.RoutePlanId">
    <option value=""></option>
    <option value="plan-a">Dialysis run</option>
    <option value="plan-b" data-available="false">Old run (no longer available)</option>
  </select>
  <a id="callRoutePreviewLink" href="#" style="display: none;">Preview</a>
  <button type="submit" id="update">Update</button>
  <button type="submit" id="dispatchNow" name="DispatchNow" value="true">Save and dispatch now</button>
</form>`;

async function harness(browser) {
    const context = await browser.newContext();
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.setContent(form);
    await page.addScriptTag({ path: jquery });
    await page.evaluate(() => {
        window.resgrid = { absoluteBaseUrl: '' };
        window.callLocationsConfig = { page: 'editcall', previewUrl: '/preview', text: { plusCodeNotFound: 'No plus code' } };
        window.fetches = [];
        window.fetch = function (url) {
            return new Promise(function (resolve, reject) { window.fetches.push({ url: url, resolve: resolve, reject: reject }); });
        };
        window.calls = [];
        $.ajax = function (options) { var d = $.Deferred(); window.calls.push({ options: options, d: d }); return d.promise(); };
        window.alerts = [];
        window.alert = function (message) { window.alerts.push(message); };
        window.searches = 0;
        $('#searchButton').on('click', function (evt) { evt.preventDefault(); window.searches++; });
        // The form's submissions that got through, with the button that sent them; never navigates.
        window.submitted = [];
        document.addEventListener('submit', function (evt) {
            window.submitted.push(evt.submitter ? evt.submitter.id : null);
            evt.preventDefault();
        });
        window.map = {
            lines: 0, markers: 0,
            addLine: function () { this.lines++; return { remove: function () {}, bounds: function () { return null; } }; },
            addMarker: function () { this.markers++; return { remove: function () {}, onDragEnd: function () {} }; },
            setView: function () {}, getZoom: function () { return 12; }, fitBounds: function () {}
        };
    });
    await page.addScriptTag({ path: script });
    await page.evaluate(() => resgrid.dispatch.calllocations.mapReady(window.map));
    return { context, page, errors };
}

// The fetch promises are thenables, so the page functions return nothing (evaluate would otherwise await them).
const answer = (page, index, data) => page.evaluate(([i, d]) => {
    window.fetches[i].resolve({ ok: true, status: 200, json: function () { return Promise.resolve({ Data: d }); } });
}, [index, data]);
const fetchCount = (page) => page.evaluate(() => window.fetches.length);
const fetchUrl = (page, index) => page.evaluate((i) => window.fetches[i].url, index);
const value = (page, selector) => page.inputValue(selector);
const settle = (page) => page.waitForTimeout(50);

(async () => {
    const browser = await chromium.launch(launch.launchOptions());
    try {
        // An address lookup that answers after a newer one, or after the destination was cleared, changes nothing.
        {
            const { context, page, errors } = await harness(browser);
            await page.fill('#Call_DestinationAddress', 'Old Hospital');
            await page.click('#destinationSearchButton');
            await page.fill('#Call_DestinationAddress', 'New Hospital');
            await page.click('#destinationSearchButton');
            assert.equal(await fetchCount(page), 2);

            await answer(page, 1, { Latitude: 51.05, Longitude: 3.72 });
            await settle(page);
            assert.equal(await value(page, '#DestinationLatitude'), '51.05');

            await answer(page, 0, { Latitude: 40.1, Longitude: -105.1 });
            await settle(page);
            assert.equal(await value(page, '#DestinationLatitude'), '51.05', 'the older lookup does not overwrite the newer one');
            assert.equal(await value(page, '#DestinationLongitude'), '3.72');

            await page.click('#destinationSearchButton');
            await page.click('#destinationClearButton');
            await answer(page, 2, { Latitude: 51.05, Longitude: 3.72 });
            await settle(page);
            assert.equal(await value(page, '#DestinationLatitude'), '', 'a lookup that answers after Clear does not fill the destination back in');
            assert.equal(await value(page, '#Call_DestinationAddress'), '');
            assert.deepEqual(errors, []);
            await context.close();
        }

        // A save with an unresolved plus code waits for it: found, the point replaces the code and the form goes on with the pressed button.
        {
            const { context, page, errors } = await harness(browser);
            await page.fill('#DestinationPlusCode', '9C4QXP8M+2Q');
            await page.click('#dispatchNow');
            assert.deepEqual(await page.evaluate(() => window.submitted), [], 'the save waits for the plus code');
            assert.equal(await fetchCount(page), 1);
            assert.match(await fetchUrl(page, 0), /PlusCodeLookup\?code=9C4QXP8M%2B2Q/);

            await answer(page, 0, { Latitude: 51.0521, Longitude: 3.7256 });
            await page.waitForFunction(() => window.submitted.length === 1);
            assert.deepEqual(await page.evaluate(() => window.submitted), ['dispatchNow'], 'sent with the button that was pressed');
            assert.equal(await value(page, '#DestinationPlusCode'), '', 'a plus code is never stored');
            assert.equal(await value(page, '#DestinationLatitude'), '51.0521');
            assert.equal(await value(page, '#DestinationLongitude'), '3.7256');
            assert.deepEqual(errors, []);
            await context.close();
        }

        // Not found: the dispatcher is told and the form is not sent.
        {
            const { context, page } = await harness(browser);
            await page.fill('#DestinationPlusCode', 'NOTACODE');
            await page.click('#update');
            await answer(page, 0, null);
            await settle(page);
            assert.deepEqual(await page.evaluate(() => window.alerts), ['No plus code']);
            assert.deepEqual(await page.evaluate(() => window.submitted), []);
            assert.equal(await value(page, '#DestinationPlusCode'), 'NOTACODE');

            // Cleared by the dispatcher, the form goes as it is.
            await page.fill('#DestinationPlusCode', '');
            await page.click('#update');
            assert.deepEqual(await page.evaluate(() => window.submitted), ['update']);
            await context.close();
        }

        // A route preview still on its way is not drawn once a route that can no longer be followed is picked.
        {
            const { context, page, errors } = await harness(browser);
            await page.selectOption('#Call_RoutePlanId', 'plan-a');
            assert.equal(await page.evaluate(() => window.calls.length), 1);
            await page.selectOption('#Call_RoutePlanId', 'plan-b');
            assert.equal(await page.evaluate(() => window.calls.length), 1, 'nothing to preview for an unavailable route');

            await page.evaluate(() => window.calls[0].d.resolve({
                success: true, color: '#ff0000', stops: [{ order: 1, name: 'Stop 1', lat: 39.75, lng: -104.95 }],
                start: { lat: 39.70, lng: -104.90 }, end: { lat: 39.80, lng: -105.00 }
            }));
            await settle(page);
            assert.equal(await page.evaluate(() => window.map.lines), 0, 'the earlier route\'s line is not drawn under the unavailable one');
            assert.equal(await page.evaluate(() => window.map.markers), 0);
            assert.deepEqual(errors, []);
            await context.close();
        }

        // A location POI with an address but no point: the old pin's coordinates go and the address is looked up.
        {
            const { context, page, errors } = await harness(browser);
            await page.selectOption('#Call_LocationPoiId', '6');
            assert.equal(await value(page, '#Call_Address'), '9 Clinic Rd');
            assert.equal(await value(page, '#Latitude'), '');
            assert.equal(await value(page, '#Longitude'), '');
            assert.equal(await page.evaluate(() => window.searches), 1);

            // A POI with a point places the pin itself, without a lookup.
            await page.selectOption('#Call_LocationPoiId', '5');
            assert.equal(await value(page, '#Latitude'), '39.74');
            assert.equal(await page.evaluate(() => window.searches), 1);
            assert.deepEqual(errors, []);
            await context.close();
        }

        console.log('call-locations: all checks passed');
    } finally {
        await browser.close();
    }
})().catch(error => {
    console.error(error);
    process.exit(1);
});
