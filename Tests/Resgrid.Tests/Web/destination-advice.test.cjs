// The View Call / Update Call Enhanced AI destination advice panel (resgrid.dispatch.destinationadvice.js, plan D4): a read that
// started before an action never paints over the action's result (a dismissed advice must not come back), a refresh announced while
// an action is posting is run after it (whether the action worked or failed), and a refused apply shows why and keeps the advice.
// jQuery is real; the server is stubbed.
//   node Tests/Resgrid.Tests/Web/destination-advice.test.cjs   (or through BrowserScriptTests / npm test)
// See browser-launch.cjs for RESGRID_PLAYWRIGHT_PATH and RESGRID_PLAYWRIGHT_CHANNEL.
const assert = require('node:assert/strict');
const path = require('node:path');
const launch = require('./browser-launch.cjs');
const { chromium } = launch.playwright();
const root = path.resolve(__dirname, '../../..');
const jquery = path.join(root, 'Web/Resgrid.Web/wwwroot/lib/jquery/dist/jquery.min.js');
const script = path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/internal/dispatch/resgrid.dispatch.destinationadvice.js');

// The partial's markup (_DestinationAdvice.cshtml), Update Call layout.
const panel = `
<div class="form-group" id="destinationAdvicePanel" style="display: none;" data-call-id="900" data-mode="form" data-start-hub="false" data-free-destination="false">
  <button type="button" id="destinationAdviceAsk" style="display: none;"><span>Ask</span></button>
  <div id="destinationAdviceBody"></div>
  <div id="destinationAdviceActions" style="display: none;">
    <button type="button" id="destinationAdviceApply">Apply</button><button type="button" id="destinationAdviceDismiss">Dismiss</button>
  </div>
  <div id="destinationAdviceMessage" style="display: none;"></div>
  <div style="display: none;"><input name="__RequestVerificationToken" type="hidden" value="af-token"></div>
</div>
<select id="Call_DestinationPoiId"><option value="10">Community Hospital</option></select>`;

const strings = {
    ask: 'Ask', asking: 'Asking', dismissReason: 'Why?', consider: 'Consider {0}', keep: 'Keep', noSuggestion: 'No suggestion', current: 'Current: {0}',
    currentNone: 'No destination', currentFree: 'Free destination', needs: 'Needs {0}', confidence: 'Confidence {0}%', advisedAt: 'Advised {0}',
    lowConfidence: 'Low confidence', noAdvice: 'No advice yet', applied: 'Destination set to {0}', dismissed: 'Dismissed', failed: 'Failed',
    status1: 'limited', status2: 'diversion', status3: 'closed',
    outcomes: { DestinationClosed: 'The recommended destination is closed now.', CallChanged: 'The call changed.' }
};

const advice = {
    id: 'adv-1', callId: 900, currentPoiId: 10, currentPoiName: 'Community Hospital', recommendedPoiId: 20, recommendedPoiName: 'Regional Trauma Center',
    recommendedPoiStatus: null, requiredCapabilities: [{ id: 'c1', name: 'Level I Trauma' }], rationale: 'Level I trauma.', confidence: 0.8, isChange: true,
    isConfident: true, state: 1, createdOn: '2026-11-20T15:00:00Z', trigger: 4
};
const withAdvice = { success: true, available: true, canApply: true, advice };
const withoutAdvice = { success: true, available: true, canApply: true, advice: null };

async function harness(browser) {
    const context = await browser.newContext();
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.setContent(panel);
    await page.addScriptTag({ path: jquery });
    await page.evaluate((s) => {
        window.resgrid = { absoluteBaseUrl: '' };
        window.destinationAdviceStrings = s;
        window.calls = [];
        window.prompt = function () { return 'Not needed'; };
        $.ajax = function (options) { var d = $.Deferred(); window.calls.push({ options: options, d: d }); return d.promise(); };
    }, strings);
    await page.addScriptTag({ path: script });
    await page.waitForFunction(() => window.calls.length === 1);
    return { context, page, errors };
}

// The deferreds are thenables, so the page functions return nothing (evaluate would otherwise await them).
const respond = (page, index, body) => page.evaluate(([i, b]) => { window.calls[i].d.resolve(b); }, [index, body]);
const reject = (page, index) => page.evaluate((i) => { window.calls[i].d.reject({ status: 500 }); }, index);
const call = (page, index) => page.evaluate((i) => ({ url: window.calls[i].options.url, type: window.calls[i].options.type, data: window.calls[i].options.data }), index);
const callCount = (page) => page.evaluate(() => window.calls.length);
const announce = (page) => page.evaluate(() => document.dispatchEvent(new CustomEvent('resgrid:ai-destination-advice-updated', { detail: { callId: 900 } })));
const body = (page) => page.textContent('#destinationAdviceBody');

(async () => {
    const browser = await chromium.launch(launch.launchOptions());
    try {
        // A read that started before Dismiss and answers after it does not bring the dismissed advice back.
        {
            const { context, page, errors } = await harness(browser);
            await respond(page, 0, withAdvice);
            assert.match(await body(page), /Consider Regional Trauma Center/);

            await announce(page);
            await page.waitForFunction(() => window.calls.length === 2);
            assert.equal((await call(page, 1)).url, '/User/AiDestinations/Advice');

            await page.click('#destinationAdviceDismiss');
            const dismiss = await call(page, 2);
            assert.equal(dismiss.url, '/User/AiDestinations/Dismiss');
            assert.deepEqual(dismiss.data, { adviceId: 'adv-1', reason: 'Not needed', __RequestVerificationToken: 'af-token' });
            await respond(page, 2, { success: true, outcome: 'Dismissed', advice: Object.assign({}, advice, { state: 3 }) });
            assert.match(await body(page), /No advice yet/);

            // The stale read answers with the old open advice: ignored.
            await respond(page, 1, withAdvice);
            assert.match(await body(page), /No advice yet/, 'a read from before the dismissal is discarded');
            assert.ok(!(await page.isVisible('#destinationAdviceActions')));

            // The change that stale read was fetching is read again after the action.
            assert.equal(await callCount(page), 4);
            assert.equal((await call(page, 3)).url, '/User/AiDestinations/Advice');
            await respond(page, 3, withoutAdvice);
            assert.match(await body(page), /No advice yet/);
            assert.deepEqual(errors, []);
            await context.close();
        }

        // A refresh announced while an action posts is not lost: it runs once the action ends, even when the action failed.
        {
            const { context, page, errors } = await harness(browser);
            await respond(page, 0, withAdvice);

            await page.click('#destinationAdviceApply');
            assert.equal((await call(page, 1)).url, '/User/AiDestinations/Apply');
            await announce(page);
            await page.waitForTimeout(700);
            assert.equal(await callCount(page), 2, 'no read while the action is posting');

            await reject(page, 1);
            assert.equal(await page.textContent('#destinationAdviceMessage'), 'Failed');
            assert.equal(await callCount(page), 3, 'the held refresh runs after the failed action');
            assert.equal((await call(page, 2)).url, '/User/AiDestinations/Advice');
            const updated = Object.assign({}, advice, { id: 'adv-2', recommendedPoiId: 30, recommendedPoiName: 'County Medical' });
            await respond(page, 2, Object.assign({}, withAdvice, { advice: updated }));
            assert.match(await body(page), /Consider County Medical/, 'the other dispatcher\'s change is shown');
            assert.deepEqual(errors, []);
            await context.close();
        }

        // An apply refused because the destination closed shows why and keeps the open advice and its actions.
        {
            const { context, page } = await harness(browser);
            await respond(page, 0, withAdvice);
            await page.click('#destinationAdviceApply');
            await respond(page, 1, { success: false, outcome: 'DestinationClosed', canApply: true, advice });
            assert.equal(await page.textContent('#destinationAdviceMessage'), 'The recommended destination is closed now.');
            assert.ok(await page.isVisible('#destinationAdviceActions'));
            assert.equal(await page.inputValue('#Call_DestinationPoiId'), '10', 'the form keeps its destination');
            assert.equal(await callCount(page), 2);
            await context.close();
        }
    } finally {
        await browser.close();
    }
    console.log('destination-advice: ok');
})().catch((error) => {
    console.error(error);
    process.exit(1);
});
