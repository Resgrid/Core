// The View Call / Update Call text conversation panel (resgrid.dispatch.intakeconversation.js, plan section 8): the thread renders
// the texter's words as plain text, the texter's number as the server sent it (masked for viewers), who answers now, Take / Release /
// End for editors, a send box that sends on Enter (Shift+Enter is a new line) within the length limit, take-over only after a
// confirmation, and a reload on the intakeConversationUpdated eventing DOM event for this call only. jQuery is real; the server is stubbed.
//   node Tests/Resgrid.Tests/Web/intake-conversation.test.cjs   (or through BrowserScriptTests / npm test)
// See browser-launch.cjs for RESGRID_PLAYWRIGHT_PATH and RESGRID_PLAYWRIGHT_CHANNEL.
const assert = require('node:assert/strict');
const path = require('node:path');
const launch = require('./browser-launch.cjs');
const { chromium } = launch.playwright();
const root = path.resolve(__dirname, '../../..');
const jquery = path.join(root, 'Web/Resgrid.Web/wwwroot/lib/jquery/dist/jquery.min.js');
const script = path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/internal/dispatch/resgrid.dispatch.intakeconversation.js');

// The partial's markup (_IntakeConversation.cshtml), View Call layout.
const panel = `
<meta name="request-verification-token" content="af-token">
<div class="row" id="intakeConversationPanel" style="display: none;" data-call-id="900" data-start-hub="false" data-max-length="20">
  <span id="intakeConversationState"></span><span id="intakeConversationHandler"></span><span id="intakeConversationTexter"></span>
  <div id="intakeConversationThread"></div><div id="intakeConversationNotice" style="display: none;"></div>
  <div id="intakeConversationActions" style="display: none;">
    <button type="button" id="intakeConversationTake">Take</button><button type="button" id="intakeConversationRelease">Release</button>
    <input type="checkbox" id="intakeConversationNotify"><button type="button" id="intakeConversationClose">End</button>
  </div>
  <div id="intakeConversationCompose" style="display: none;">
    <textarea id="intakeConversationText"></textarea><span id="intakeConversationCounter"></span><button type="button" id="intakeConversationSend">Send</button>
  </div>
</div>`;

const strings = {
    stateOpen: 'Open', stateClosed: 'Ended', handlerAssistant: 'AI asking', handlerUnassisted: 'Waiting for a dispatcher', handlerDispatcher: 'Connected with {0}',
    handlerDispatcherYou: 'You are connected', texter: 'Texter: {0}', optedOut: 'Opted out', closedReason: 'Ended: {0}', authorCaller: 'Caller',
    authorAssistant: 'AI assistant', authorSystem: 'Automatic message', authorDispatcher: 'Dispatcher', statusQueued: 'Sending', statusSent: 'Sent',
    statusFailed: 'Not delivered', textUnavailable: '(text unavailable)', noMessages: 'No messages', takeOverConfirm: 'Take it over?', closeConfirm: 'End?',
    failed: 'Failed', outcomes: { InvalidMessage: 'Enter 1 to {0} characters.', HeldByAnotherDispatcher: 'Held by another dispatcher.' },
    closeReasons: { Idle: 'No messages for a while' }
};

function thread(overrides) {
    const conversation = Object.assign({
        id: 'conv-1', callId: 900, state: 1, handler: 2, assignedUserId: null, assignedName: null, isMine: false, participant: '***0199', optedOut: false,
        closeReason: null
    }, overrides && overrides.conversation);
    return Object.assign({
        success: true, exists: true, canEdit: true, canConnect: true, maxLength: 20, conversation,
        messages: [
            { id: 'm1', direction: 1, author: 1, authorName: null, text: '<img src=x onerror=alert(1)> crash on Route 9', status: 1, createdOn: '2026-11-20T15:00:00Z' },
            { id: 'm2', direction: 2, author: 4, authorName: null, text: 'Springfield Fire: You texted the dispatch number', status: 4, createdOn: '2026-11-20T15:00:01Z' }
        ]
    }, overrides, { conversation });
}

async function harness(browser, first) {
    const context = await browser.newContext();
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.setContent(panel);
    await page.addScriptTag({ path: jquery });
    await page.evaluate((s) => {
        window.resgrid = { absoluteBaseUrl: '' };
        window.intakeConversationStrings = s;
        window.calls = [];
        window.confirms = [];
        window.confirmAnswer = true;
        window.confirm = function (text) { window.confirms.push(text); return window.confirmAnswer; };
        $.ajax = function (options) { var d = $.Deferred(); window.calls.push({ options: options, d: d }); return d.promise(); };
    }, strings);
    await page.addScriptTag({ path: script });
    await page.waitForFunction(() => window.calls.length === 1);
    await respond(page, 0, first);
    return { context, page, errors };
}

const respond = (page, index, body) => page.evaluate(([i, b]) => window.calls[i].d.resolve(b), [index, body]);
const call = (page, index) => page.evaluate((i) => ({ url: window.calls[i].options.url, type: window.calls[i].options.type, data: window.calls[i].options.data }), index);
const callCount = (page) => page.evaluate(() => window.calls.length);
const visible = (page, id) => page.isVisible('#' + id);

(async () => {
    const browser = await chromium.launch(launch.launchOptions());
    try {
        // Rendering: plain text only, the masked number as sent, who answers now, a failed delivery flagged.
        {
            const { context, page, errors } = await harness(browser, thread());
            const first = await call(page, 0);
            assert.equal(first.url, '/User/IntakeConversations/Thread');
            assert.deepEqual(first.data, { callId: 900 });
            assert.ok(await visible(page, 'intakeConversationPanel'));
            assert.equal(await page.evaluate(() => document.querySelectorAll('#intakeConversationThread img').length), 0, 'texter text is never HTML');
            assert.match(await page.textContent('#intakeConversationThread'), /<img src=x onerror=alert\(1\)> crash on Route 9/);
            assert.match(await page.textContent('#intakeConversationThread'), /Not delivered/);
            assert.equal(await page.textContent('#intakeConversationTexter'), 'Texter: ***0199');
            assert.equal(await page.textContent('#intakeConversationHandler'), 'Waiting for a dispatcher');
            assert.ok(await visible(page, 'intakeConversationTake'));
            assert.ok(!(await visible(page, 'intakeConversationRelease')));
            assert.ok(await visible(page, 'intakeConversationCompose'), 'with nobody connected, sending connects first');
            assert.deepEqual(errors, []);
            await context.close();
        }

        // Enter sends (with the antiforgery token), Shift+Enter does not, the counter tracks the length and the limit is enforced.
        {
            const { context, page } = await harness(browser, thread());
            await page.fill('#intakeConversationText', 'Where are you?');
            assert.equal(await page.textContent('#intakeConversationCounter'), '14/20');
            await page.press('#intakeConversationText', 'Shift+Enter');
            assert.equal(await callCount(page), 1, 'Shift+Enter adds a line');
            await page.fill('#intakeConversationText', 'Where are you?');
            await page.press('#intakeConversationText', 'Enter');
            const sent = await call(page, 1);
            assert.equal(sent.url, '/User/IntakeConversations/Send');
            assert.equal(sent.type, 'POST');
            assert.deepEqual(sent.data, { text: 'Where are you?', conversationId: 'conv-1', __RequestVerificationToken: 'af-token' });
            await respond(page, 1, thread({ outcome: 'Done', conversation: { handler: 3, assignedUserId: 'me', isMine: true } }));
            assert.equal(await page.inputValue('#intakeConversationText'), '', 'a sent text clears the box');
            assert.equal(await page.textContent('#intakeConversationHandler'), 'You are connected');
            assert.ok(await visible(page, 'intakeConversationRelease'));

            await page.fill('#intakeConversationText', 'x'.repeat(21));
            await page.press('#intakeConversationText', 'Enter');
            assert.equal(await callCount(page), 2, 'an over-length text is not posted');
            assert.equal(await page.textContent('#intakeConversationNotice'), 'Enter 1 to 20 characters.');
            await context.close();
        }

        // Another dispatcher holds it: no send box, Take asks first and then takes over; a refusal is shown.
        {
            const { context, page } = await harness(browser, thread({ conversation: { handler: 3, assignedUserId: 'other', assignedName: 'Sam Ray', isMine: false } }));
            assert.equal(await page.textContent('#intakeConversationHandler'), 'Connected with Sam Ray');
            assert.ok(!(await visible(page, 'intakeConversationCompose')));
            await page.evaluate(() => { window.confirmAnswer = false; });
            await page.click('#intakeConversationTake');
            assert.equal(await callCount(page), 1, 'declining the confirmation does nothing');
            await page.evaluate(() => { window.confirmAnswer = true; });
            await page.click('#intakeConversationTake');
            const take = await call(page, 1);
            assert.equal(take.url, '/User/IntakeConversations/Connect');
            assert.equal(take.data.takeOver, true);
            await respond(page, 1, thread({ outcome: 'HeldByAnotherDispatcher', success: false, conversation: { handler: 3, assignedUserId: 'other', isMine: false } }));
            assert.equal(await page.textContent('#intakeConversationNotice'), 'Held by another dispatcher.');
            await context.close();
        }

        // End posts the notify choice; a closed thread shows why and offers no actions.
        {
            const { context, page } = await harness(browser, thread());
            await page.check('#intakeConversationNotify');
            await page.click('#intakeConversationClose');
            const end = await call(page, 1);
            assert.equal(end.url, '/User/IntakeConversations/Close');
            assert.equal(end.data.notify, true);
            await respond(page, 1, thread({ outcome: 'Done', conversation: { state: 2, closeReason: 'Idle' } }));
            assert.equal(await page.textContent('#intakeConversationState'), 'Ended');
            assert.equal(await page.textContent('#intakeConversationHandler'), 'Ended: No messages for a while');
            assert.ok(!(await visible(page, 'intakeConversationActions')));
            assert.ok(!(await visible(page, 'intakeConversationCompose')));
            await context.close();
        }

        // Live refresh: only this call's event reloads the thread; a call without a conversation keeps the panel hidden.
        {
            const { context, page } = await harness(browser, thread());
            await page.evaluate(() => document.dispatchEvent(new CustomEvent('resgrid:intake-conversation-updated', { detail: { callId: '901', payload: '{}' } })));
            await page.waitForTimeout(600);
            assert.equal(await callCount(page), 1);
            await page.evaluate(() => document.dispatchEvent(new CustomEvent('resgrid:intake-conversation-updated', { detail: { callId: '900', payload: '{}' } })));
            await page.waitForFunction(() => window.calls.length === 2);
            assert.equal((await call(page, 1)).url, '/User/IntakeConversations/Thread');
            await context.close();

            const none = await harness(browser, { success: true, exists: false });
            assert.ok(!(await visible(none.page, 'intakeConversationPanel')));
            await none.context.close();
        }

        // A text typed while the previous one is sending stays in the box (only the sent text is cleared).
        {
            const { context, page } = await harness(browser, thread());
            await page.fill('#intakeConversationText', 'Where are you?');
            await page.press('#intakeConversationText', 'Enter');
            await page.fill('#intakeConversationText', 'Is anyone hurt?');
            await respond(page, 1, thread({ outcome: 'Done', conversation: { handler: 3, assignedUserId: 'me', isMine: true } }));
            assert.equal(await page.inputValue('#intakeConversationText'), 'Is anyone hurt?', 'the newer draft is kept');
            await context.close();
        }

        // An update event during an action is not lost: the thread is read again once the action ends.
        {
            const { context, page } = await harness(browser, thread());
            await page.click('#intakeConversationTake');
            await page.evaluate(() => document.dispatchEvent(new CustomEvent('resgrid:intake-conversation-updated', { detail: { callId: '900', payload: '{}' } })));
            await page.waitForTimeout(600);
            assert.equal(await callCount(page), 2, 'no read while the action runs');
            await respond(page, 1, thread({ outcome: 'Done', conversation: { handler: 3, assignedUserId: 'me', isMine: true } }));
            await page.waitForFunction(() => window.calls.length === 3);
            assert.equal((await call(page, 2)).url, '/User/IntakeConversations/Thread');
            await context.close();
        }

        // A read that was overtaken by an action (or a newer read) never puts older state back on screen.
        {
            const { context, page } = await harness(browser, thread());
            await page.evaluate(() => document.dispatchEvent(new CustomEvent('resgrid:intake-conversation-updated', { detail: { callId: '900', payload: '{}' } })));
            await page.waitForFunction(() => window.calls.length === 2);
            await page.click('#intakeConversationTake');
            await respond(page, 2, thread({ outcome: 'Done', conversation: { handler: 3, assignedUserId: 'me', isMine: true } }));
            assert.equal(await page.textContent('#intakeConversationHandler'), 'You are connected');
            await respond(page, 1, thread());
            assert.equal(await page.textContent('#intakeConversationHandler'), 'You are connected', 'the stale read is dropped');
            await context.close();
        }

        console.log('intake conversation panel: all checks passed');
    } finally {
        await browser.close();
    }
})().catch(error => {
    console.error(error);
    process.exit(1);
});
