// Browser push service worker (wwwroot/rg-push-sw.js).
//
// No browser can be handed a real push here, so the worker runs in a node vm with a stand-in `self`.
// These cases pin what Core sends (NovuProvider.SendNotification's FCM webpush block: data carries
// title, message, eventCode, type, category) to what the person sees, and where a click takes them:
// a tab already showing the target comes forward, anything else opens a new tab so a tab mid-form is
// never navigated away from.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname, '../../../Web/Resgrid.Web/wwwroot/rg-push-sw.js'), 'utf8');
const origin = 'https://resgrid.test';

function loadWorker(windows = []) {
    const listeners = {};
    const shown = [];
    const opened = [];
    const focused = [];

    const self = {
        location: new URL(`${origin}/rg-push-sw.js`),
        addEventListener: (type, handler) => { listeners[type] = handler; },
        skipWaiting: () => Promise.resolve(),
        registration: {
            showNotification: (title, options) => { shown.push({ title, options }); return Promise.resolve(); },
        },
        clients: {
            claim: () => Promise.resolve(),
            matchAll: () => Promise.resolve(windows.map((url) => ({ url, focus() { focused.push(url); return Promise.resolve(this); } }))),
            openWindow: (url) => { opened.push(url); return Promise.resolve(null); },
        },
    };

    vm.runInNewContext(source, { self, URL, String, console });

    async function dispatch(type, event) {
        let pending = Promise.resolve();
        listeners[type]({ ...event, waitUntil: (promise) => { pending = promise; } });
        await pending;
    }

    return {
        shown,
        opened,
        focused,
        push: (payload) => dispatch('push', { data: payload === undefined ? null : { json: () => payload, text: () => JSON.stringify(payload) } }),
        pushText: (text) => dispatch('push', { data: { json: () => { throw new SyntaxError('not json'); }, text: () => text } }),
        click: (data) => dispatch('notificationclick', { notification: { data, close() {} } }),
    };
}

// What an FCM web token receives for a Novu trigger: Firebase's notification block plus our data map.
function fcmPush(eventCode, category, title = 'Structure Fire', message = '123 Main St') {
    return {
        from: '343968022249',
        fcmMessageId: 'abc',
        notification: { title, body: message, tag: eventCode },
        data: { title, message, eventCode, type: '3', category },
    };
}

(async () => {
    {
        const worker = loadWorker();
        await worker.push(fcmPush('C1234', 'calls'));
        assert.equal(worker.shown.length, 1);
        const { title, options } = worker.shown[0];
        assert.equal(title, 'Structure Fire');
        assert.equal(options.body, '123 Main St');
        assert.equal(options.tag, 'C1234', 'a repeat push for one call replaces its notification');
        assert.equal(options.requireInteraction, true, 'a call stays up until dealt with');
        assert.equal(options.data.url, '/User/Dispatch/ViewCall?callId=1234');
        console.log('ok - a call push shows the call and links to it');
    }

    {
        const worker = loadWorker();
        await worker.push(fcmPush('M:77', 'messages', 'New Message', 'Shift swap'));
        const { options } = worker.shown[0];
        assert.equal(options.requireInteraction, false);
        assert.equal(options.data.url, '/User/Messages/ViewMessage?messageId=77');
        console.log('ok - a message push links to the message');
    }

    {
        const routes = {
            't:abc-123': '/User/Chat',
            'g:abc-123': '/User/Chat',
            'W:5': '/User/WeatherAlerts',
            'N42': '/User/Home/Dashboard',
            'NWO:9': '/User/Home/Dashboard',
            'NA:tx-1': '/User/Home/Dashboard',
            'CT:token-1': '/User/Home/Dashboard',
            'C:not-a-number': '/User/Dispatch/Dashboard',
            '': '/User/Home/Dashboard',
        };

        for (const [eventCode, url] of Object.entries(routes)) {
            const worker = loadWorker();
            await worker.push(fcmPush(eventCode, 'notifications'));
            assert.equal(worker.shown[0].options.data.url, url, `route for '${eventCode}'`);
        }

        console.log('ok - chat, weather, notification and unknown codes route to their pages');
    }

    {
        const worker = loadWorker();
        await worker.push({ notification: { title: 'Only notification', body: 'No data block' } });
        assert.equal(worker.shown[0].title, 'Only notification');
        assert.equal(worker.shown[0].options.body, 'No data block');

        const empty = loadWorker();
        await empty.push(undefined);
        assert.equal(empty.shown[0].title, 'Resgrid', 'every push shows something, or the browser shows its own warning');

        const text = loadWorker();
        await text.pushText('plain text body');
        assert.equal(text.shown[0].options.body, 'plain text body');
        console.log('ok - a push without our data block still shows a notification');
    }

    {
        const target = `${origin}/User/Dispatch/ViewCall?callId=1234`;
        const worker = loadWorker([`${origin}/User/Home/Dashboard`, target]);
        await worker.click({ eventCode: 'C1234', url: '/User/Dispatch/ViewCall?callId=1234' });
        assert.deepEqual(worker.focused, [target]);
        assert.deepEqual(worker.opened, []);
        console.log('ok - a click brings forward a tab already showing the call');
    }

    {
        const worker = loadWorker([`${origin}/User/Personnel/EditPerson?userId=1`]);
        await worker.click({ eventCode: 'C1234', url: '/User/Dispatch/ViewCall?callId=1234' });
        assert.deepEqual(worker.focused, [], 'the tab mid-edit is left alone');
        assert.deepEqual(worker.opened, [`${origin}/User/Dispatch/ViewCall?callId=1234`]);
        console.log('ok - a click opens the call in a new tab rather than navigating an open one');
    }
    for (const url of ['https://untrusted.test/call', '//untrusted.test/call', 'javascript:alert(1)', 'data:text/html,redirect', 'https://[invalid']) {
        const worker = loadWorker();
        await worker.click({ url });
        assert.deepEqual(worker.opened, [`${origin}/User/Home/Dashboard`], `invalid or external notification target: ${url}`);
    }
    console.log('ok - notification clicks stay on this origin and malformed targets use the dashboard');
})().catch((error) => {
    console.error(error);
    process.exit(1);
});
