// Facility maps on the New Call / Update Call map (resgrid.dispatch.facilitymaps.js): the department's custom and indoor
// maps are offered in a map control; picking one draws its layer as same-origin tiles (through /api/web-bff) and its
// rooms; a click inside a room picks the smallest dispatchable room under it (never a corridor) and fills the call's
// Indoor Location, pin and (only when empty) address; a pick in the Indoor Location search moves the pin and shows the
// room's floor; Update Call opens on the floor of the call's indoor location. jQuery is real; the API and map are stubbed.
//   node Tests/Resgrid.Tests/Web/facility-maps.test.cjs   (or through BrowserScriptTests / npm test)
const assert = require('node:assert/strict');
const path = require('node:path');
const launch = require('./browser-launch.cjs');
const { chromium } = launch.playwright();
const root = path.resolve(__dirname, '../../..');
const jquery = path.join(root, 'Web/Resgrid.Web/wwwroot/lib/jquery/dist/jquery.min.js');
const script = path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/internal/dispatch/resgrid.dispatch.facilitymaps.js');

const form = `
<form id="callForm">
  <input id="Call_Address" name="Call.Address" value="">
  <input id="Latitude" name="Latitude" value="">
  <input id="Longitude" name="Longitude" value="">
  <select id="indoorZoneSearch"></select>
  <input type="hidden" id="IndoorMapZoneId" name="IndoorMapZoneId" value="">
  <input type="hidden" id="IndoorMapFloorId" name="IndoorMapFloorId" value="__FLOOR__">
</form>`;

// A building (map b100) with two floors; level 1 has a large dispatchable hall, a room inside it, and a corridor.
const square = (w, s, e, n) => ({ type: 'Polygon', coordinates: [[[w, s], [e, s], [e, n], [w, n], [w, s]]] });
const api = {
    'GetCustomMaps': { Data: [
        { IndoorMapId: 'b100', Name: 'Bldg 100', MapType: 0, DefaultFloorId: 'l1', BoundsSWLat: 32.0, BoundsSWLon: -112.0, BoundsNELat: 32.01, BoundsNELon: -111.99 },
        { IndoorMapId: 'site', Name: 'Campus plan', MapType: 1, BoundsSWLat: 31.99, BoundsSWLon: -112.01, BoundsNELat: 32.02, BoundsNELon: -111.98 },
    ] },
    'GetCustomMap/b100': { Data: { Layers: [
        { IndoorMapFloorId: 'l2', IndoorMapId: 'b100', Name: 'Level 2', FloorOrder: 1, HasImage: true, Opacity: 0.8, MercatorTileUrl: 'https://api.example/api/v4/Mapping/GetCustomMapMercatorTile/l2/{z}/{x}/{y}?sig=b' },
        { IndoorMapFloorId: 'l1', IndoorMapId: 'b100', Name: 'Level 1', FloorOrder: 0, HasImage: true, Opacity: 0.8, MercatorTileUrl: 'https://api.example/api/v4/Mapping/GetCustomMapMercatorTile/l1/{z}/{x}/{y}?sig=a' },
    ] } },
    'GetCustomMapRegionsGeoJSON/l1': { type: 'FeatureCollection', features: [
        { type: 'Feature', id: 'hall', geometry: square(-112.0, 32.0, -111.99, 32.01), properties: { id: 'hall', name: 'Assembly hall', isDispatchable: true } },
        { type: 'Feature', id: 'z14', geometry: square(-111.996, 32.004, -111.994, 32.006), properties: { id: 'z14', name: 'Formation Zone 14', isDispatchable: true } },
        { type: 'Feature', id: 'aisle', geometry: square(-111.9935, 32.0, -111.9925, 32.01), properties: { id: 'aisle', name: 'Main aisle', isDispatchable: false } },
    ] },
    'GetCustomMapRegionsGeoJSON/l2': { type: 'FeatureCollection', features: [] },
    'GetCustomMapLayer/l2': { Data: { IndoorMapFloorId: 'l2', IndoorMapId: 'b100', Name: 'Level 2' } },
    'GetCustomMapLayer/l1': { Data: { IndoorMapFloorId: 'l1', IndoorMapId: 'b100', Name: 'Level 1' } },
};

async function harness(browser, floorId) {
    const context = await browser.newContext();
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.setContent(form.replace('__FLOOR__', floorId || ''));
    await page.addScriptTag({ path: jquery });
    await page.evaluate((routes) => {
        window.requests = [];
        window.fetch = (url) => {
            window.requests.push(url);
            const key = url.replace('/api/web-bff/api/v4/Mapping/', '');
            const body = routes[key];
            return Promise.resolve({ ok: !!body, status: body ? 200 : 404, json: () => Promise.resolve(body) });
        };
        // The call map (resgrid.maps kit), recording what the script asks of it.
        window.map = {
            controls: [], rasters: [], shapes: [], fits: [],
            addControl(element) { this.controls.push(element); document.body.appendChild(element); return { remove() {} }; },
            addRasterOverlay(id, tiles, options) { const entry = { id, tiles, options, removed: false }; this.rasters.push(entry); return { remove() { entry.removed = true; } }; },
            addGeoJson(id, data) { const entry = { id, count: data.features.length, removed: false }; this.shapes.push(entry); return { remove() { entry.removed = true; } }; },
            fitBounds(points) { this.fits.push(points); },
        };
        window.pins = [];
    }, api);
    await page.addScriptTag({ path: script });
    await page.evaluate(() => resgrid.dispatch.facilitymaps.mapReady(window.map, { setLocation: (lat, lng) => window.pins.push([lat, lng]) }));
    await settle(page);
    return { context, page, errors };
}

const settle = page => page.evaluate(() => new Promise(resolve => setTimeout(resolve, 30)));
const value = (page, selector) => page.$eval(selector, el => el.value);

(async () => {
    const browser = await chromium.launch(launch.launchOptions());
    try {
        // The control lists the maps; picking a building shows its default floor as same-origin tiles plus its rooms.
        {
            const { context, page, errors } = await harness(browser);
            const options = await page.$$eval('.rg-facility-maps select:first-of-type option', els => els.map(o => o.textContent));
            assert.deepEqual(options, ['None', 'Bldg 100', 'Campus plan']);
            await page.selectOption('.rg-facility-maps select:first-of-type', 'b100');
            await settle(page);
            const raster = await page.evaluate(() => window.map.rasters[0]);
            assert.equal(raster.tiles, '/api/web-bff/api/v4/Mapping/GetCustomMapMercatorTile/l1/{z}/{x}/{y}?sig=a', 'default floor, braces kept, through the facade');
            assert.deepEqual(raster.options.bounds, [-112.0, 32.0, -111.99, 32.01], 'a layer without bounds uses its map\'s');
            assert.equal(raster.options.opacity, 0.8);
            assert.equal(await page.evaluate(() => window.map.shapes[0].count), 3);
            assert.equal(await page.evaluate(() => window.map.fits.length), 1, 'picking a map zooms to it');
            const floors = await page.$$eval('.rg-facility-maps select:nth-of-type(2) option', els => els.map(o => o.textContent));
            assert.deepEqual(floors, ['Level 1', 'Level 2'], 'floors in floor order');

            // Switching floors replaces the drawn floor without moving the camera.
            await page.selectOption('.rg-facility-maps select:nth-of-type(2)', 'l2');
            await settle(page);
            assert.equal(await page.evaluate(() => window.map.rasters[0].removed), true);
            assert.match(await page.evaluate(() => window.map.rasters[1].tiles), /\/l2\//);
            assert.equal(await page.evaluate(() => window.map.fits.length), 1);
            assert.deepEqual(errors, []);
            await context.close();
        }

        // A click inside a room picks the smallest dispatchable room; a corridor never counts; outside picks nothing.
        {
            const { context, page, errors } = await harness(browser);
            await page.selectOption('.rg-facility-maps select:first-of-type', 'b100');
            await settle(page);
            const room = await page.evaluate(() => resgrid.dispatch.facilitymaps.zoneAt(32.005, -111.995));
            assert.equal(room.id, 'z14', 'the room inside the hall wins');
            assert.equal(room.floorId, 'l1');
            assert.equal((await page.evaluate(() => resgrid.dispatch.facilitymaps.zoneAt(32.002, -111.993))).id, 'hall', 'the corridor is skipped for the hall around it');
            assert.equal(await page.evaluate(() => resgrid.dispatch.facilitymaps.zoneAt(33, -110)), null);

            await page.evaluate((r) => resgrid.dispatch.facilitymaps.selectZone(r, 32.005, -111.995), room);
            assert.equal(await value(page, '#IndoorMapZoneId'), 'z14');
            assert.equal(await value(page, '#IndoorMapFloorId'), 'l1');
            assert.equal(await value(page, '#indoorZoneSearch'), 'z14');
            assert.deepEqual(await page.evaluate(() => window.pins), [[32.005, -111.995]]);
            assert.equal(await value(page, '#Call_Address'), 'Formation Zone 14, Level 1, Bldg 100', 'an empty address takes the room');

            // Another room replaces the name the first one put in the address.
            const hall = await page.evaluate(() => resgrid.dispatch.facilitymaps.zoneAt(32.002, -111.993));
            await page.evaluate((r) => resgrid.dispatch.facilitymaps.selectZone(r, 32.002, -111.993), hall);
            assert.equal(await value(page, '#IndoorMapZoneId'), 'hall');
            assert.equal(await value(page, '#Call_Address'), 'Assembly hall, Level 1, Bldg 100', 'a room changed replaces the earlier room in the address');

            // A typed address is kept.
            await page.fill('#Call_Address', 'Gate 3');
            await page.evaluate((r) => resgrid.dispatch.facilitymaps.selectZone(r, 32.005, -111.995), room);
            assert.equal(await value(page, '#Call_Address'), 'Gate 3');
            assert.deepEqual(errors, []);
            await context.close();
        }

        // A pick in the Indoor Location search moves the pin and shows that room's floor; a result without a point keeps the pin.
        {
            const { context, page, errors } = await harness(browser);
            await page.evaluate(() => resgrid.dispatch.facilitymaps.zonePicked({ id: 'eoc', text: 'EOC', floorId: 'l2', lat: 32.0071, lng: -111.9951 }));
            await settle(page);
            assert.deepEqual(await page.evaluate(() => window.pins), [[32.0071, -111.9951]]);
            assert.equal(await value(page, '.rg-facility-maps select:first-of-type'), 'b100');
            assert.match(await page.evaluate(() => window.map.rasters[0].tiles), /\/l2\//);
            await page.evaluate(() => resgrid.dispatch.facilitymaps.zonePicked({ id: 'x', text: 'X', floorId: 'l2', lat: 0, lng: 0 }));
            await settle(page);
            assert.equal(await page.evaluate(() => window.pins.length), 1);
            assert.deepEqual(errors, []);
            await context.close();
        }

        // PR #591: a map whose layers are still loading is not drawn once another selection (here None) was made, so its rooms
        // never become the ones a click picks from.
        {
            const { context, page, errors } = await harness(browser);
            await page.evaluate(() => {
                const answer = window.fetch;
                window.heldLoads = [];
                window.fetch = (url) => url.indexOf('/GetCustomMap/') >= 0
                    ? new Promise((resolve) => window.heldLoads.push(() => resolve(answer(url))))
                    : answer(url);
            });
            await page.selectOption('.rg-facility-maps select:first-of-type', 'b100');
            await page.selectOption('.rg-facility-maps select:first-of-type', '');
            await page.evaluate(() => window.heldLoads.forEach((release) => release()));
            await settle(page);
            assert.equal(await page.evaluate(() => window.map.rasters.length), 0, 'the earlier map is not drawn over None');
            assert.equal(await page.evaluate(() => resgrid.dispatch.facilitymaps.zoneAt(32.005, -111.995)), null);
            assert.deepEqual(errors, []);
            await context.close();
        }

        // PR #591 round 2: of two floor lookups on their way (two Indoor Location picks), the later pick's floor is shown even
        // when the earlier lookup answers first.
        {
            const { context, page, errors } = await harness(browser);
            await page.evaluate(() => {
                const answer = window.fetch;
                window.heldFloors = {};
                window.fetch = (url) => url.indexOf('/GetCustomMapLayer/') >= 0
                    ? new Promise((resolve) => { window.heldFloors[url.split('/').pop()] = () => resolve(answer(url)); })
                    : answer(url);
            });
            await page.evaluate(() => resgrid.dispatch.facilitymaps.showFloor('l1', false));
            await page.evaluate(() => resgrid.dispatch.facilitymaps.showFloor('l2', false));
            await page.evaluate(() => window.heldFloors.l1());
            await settle(page);
            await page.evaluate(() => window.heldFloors.l2());
            await settle(page);
            const rasters = await page.evaluate(() => window.map.rasters.map((r) => r.tiles));
            assert.equal(rasters.length, 1, 'only the later floor is drawn');
            assert.match(rasters[0], /\/l2\//);
            assert.equal(await value(page, '.rg-facility-maps select:nth-of-type(2)'), 'l2');
            assert.deepEqual(errors, []);
            await context.close();
        }

        // Update Call: a call with an indoor location opens with that floor shown.
        {
            const { context, page, errors } = await harness(browser, 'l2');
            await settle(page);
            assert.equal(await value(page, '.rg-facility-maps select:first-of-type'), 'b100');
            assert.match(await page.evaluate(() => window.map.rasters[0].tiles), /\/l2\//);
            assert.equal(await page.evaluate(() => window.map.fits.length), 0, 'the call\'s own pin keeps the view');
            assert.deepEqual(errors, []);
            await context.close();
        }

        // No custom maps: no control, nothing drawn.
        {
            const context = await browser.newContext();
            const page = await context.newPage();
            await page.setContent(form.replace('__FLOOR__', ''));
            await page.addScriptTag({ path: jquery });
            await page.evaluate(() => {
                window.fetch = () => Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ Data: [] }) });
                window.map = { addControl() { throw new Error('no control expected'); } };
            });
            await page.addScriptTag({ path: script });
            await page.evaluate(() => resgrid.dispatch.facilitymaps.mapReady(window.map, {}));
            await settle(page);
            assert.equal(await page.$$eval('.rg-facility-maps', els => els.length), 0);
            await context.close();
        }

        console.log('facility-maps: all checks passed');
    } finally {
        await browser.close();
    }
})().catch(error => {
    console.error(error);
    process.exit(1);
});
