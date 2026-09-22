const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { playwright, launchOptions } = require('./browser-launch.cjs');
const web = path.resolve(__dirname, '../../../Web/Resgrid.Web');
const layerScript = fs.readFileSync(path.join(web, 'wwwroot/js/hydrant-map-layer.js'), 'utf8');
const leafletScript = fs.readFileSync(path.join(web, 'wwwroot/lib/leaflet/dist/leaflet.js'), 'utf8');
const leafletCss = fs.readFileSync(path.join(web, 'wwwroot/lib/leaflet/dist/leaflet.css'), 'utf8');

(async () => {
    const browser = await playwright().chromium.launch(launchOptions());
    try {
        const page = await browser.newPage();
        let calls = 0, mode = 'success';
        const points = [
            { HydrantId: 'one', HydrantNumber: '<img src=x onerror="window.hacked=true">', Latitude: 45, Longitude: -122, InService: true, FlowGpm: 1200, Color: '#1ab394' },
            { HydrantId: 'two', HydrantNumber: 'Out of service', Latitude: 45.01, Longitude: -122, InService: false, FlowGpm: 0, Color: '#000000' }
        ];
        await page.route('http://hydrants.test/**', async route => {
            if (route.request().url().endsWith('/layer')) {
                calls++;
                return route.fulfill({ status: mode === 'failure' ? 503 : mode === 'disabled' ? 404 : 200, contentType: 'application/json', body: JSON.stringify(mode === 'empty' ? [] : points) });
            }
            return route.fulfill({ contentType: 'text/html', body: '<div id="map" style="height:400px"></div><div id="second" style="height:400px"></div><rg-map><div id="react" style="height:200px"></div></rg-map>' });
        });
        async function setup() {
            await page.goto('http://hydrants.test/');
            await page.addStyleTag({ content: leafletCss });
            await page.addScriptTag({ content: leafletScript });
            await page.evaluate(() => { window.rgHydrantMap = { url: '/layer', detailsUrl: '/details', label: 'Hydrants', inService: 'In service', outOfService: 'Out of service', failure: 'Hydrants could not be loaded.', retry: 'Retry' }; });
            await page.addScriptTag({ content: layerScript });
        }
        await setup();
        await page.addScriptTag({ content: layerScript });
        await page.evaluate(() => {
            window.map = L.map('map').setView([45, -122], 12);
            window.second = L.map('second').setView([45, -122], 12);
            L.map('react').setView([45, -122], 12);
        });
        await page.waitForFunction(() => document.querySelectorAll('.leaflet-control-layers-overlays label').length === 2);
        assert.equal(calls, 1, 'one request shared across geographic maps; React skips the global hook');
        assert.equal(await page.locator('#map path.leaflet-interactive').count(), 2);
        assert.equal(await page.locator('#map path[stroke="#000000"]').count(), 1);
        await page.evaluate(() => { map.eachLayer(layer => { if (layer instanceof L.LayerGroup) layer.getLayers()[0].openPopup(); }); });
        assert.equal(await page.locator('.leaflet-popup-content img').count(), 0);
        assert.match(await page.locator('.leaflet-popup-content').innerText(), /<img src=x/);
        assert.equal(await page.evaluate(() => window.hacked), undefined);
        await page.locator('#map .leaflet-control-layers').hover();
        await page.locator('#map .leaflet-control-layers-selector').uncheck();
        assert.equal(await page.locator('#map path.leaflet-interactive').count(), 0);
        await page.locator('#map .leaflet-control-layers-selector').check();
        assert.equal(await page.locator('#map path.leaflet-interactive').count(), 2);
        assert.equal(calls, 1);
        mode = 'failure';
        await setup();
        await page.evaluate(() => { window.map = L.map('map').setView([45, -122], 12); });
        await page.getByRole('button', { name: 'Retry', exact: true }).waitFor();
        mode = 'success';
        await page.getByRole('button', { name: 'Retry', exact: true }).click();
        await page.waitForFunction(() => document.querySelectorAll('#map path.leaflet-interactive').length === 2);
        mode = 'empty';
        await setup();
        await page.evaluate(() => { window.map = L.map('map').setView([45, -122], 12); });
        await page.locator('.leaflet-control-layers-overlays label').waitFor({ state: 'attached' });
        assert.equal(await page.locator('path.leaflet-interactive').count(), 0);
        mode = 'disabled';
        await setup();
        const before = calls;
        await page.evaluate(() => { window.map = L.map('map', { crs: L.CRS.Simple }).setView([0, 0], 1); });
        await page.waitForTimeout(100);
        assert.equal(calls, before, 'pixel-based floor plans must not fetch geographic hydrants');
        const disabledResponse = page.waitForResponse(response => response.url().endsWith('/layer'));
        await page.evaluate(() => { L.map('second').setView([45, -122], 12); });
        await disabledResponse;
        assert.equal(await page.locator('.leaflet-control-layers').count(), 0, 'disabled module has no overlay');
        console.log('PASS: hydrant overlays, duplicate installation guard, shared request, toggle, status colors, safe popup, retry, empty/disabled modules and indoor exclusion.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
