const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createRequire } = require('node:module');
const apps = path.resolve(__dirname, '../../../Web/Resgrid.Web/Areas/User/Apps');
const ts = createRequire(path.join(apps, 'package.json'))('typescript');
const source = fs.readFileSync(path.join(apps, 'src/runtime/webPush.ts'), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS } }).outputText;
const key = 'rg.webPush.registration';
const config = { apiKey: 'key', projectId: 'project', messagingSenderId: 'sender', appId: 'app', vapidKey: 'vapid', userId: 'user', departmentId: '12' };
function load({ failRegister = false, failUnregister = false, token = 'new', userId = 'user' } = {}) {
    const previous = { userId, departmentId: '12', token: 'old', registeredAt: Date.now() - 2 * 86400000 };
    const storage = new Map([[key, JSON.stringify(previous)], ['rg.webPush.device', JSON.stringify('device')]]);
    const calls = [], events = [];
    const worker = { active: { scriptURL: 'https://resgrid.test/rg-push-sw.js' } };
    const firebase = { isSupported: async () => true, getMessaging: () => ({}),
        getToken: async () => { events.push('mint'); return token; }, deleteToken: async () => { events.push('delete'); return true; } };
    const exports = {};
    vm.runInNewContext(compiled, { exports, require(name) {
        if (name === './api') return { apiFetchJson: async (url, init) => {
            calls.push({ url, body: JSON.parse(init.body) });
            if ((failRegister && url.endsWith('/RegisterDevice')) || (failUnregister && url.endsWith('/UnRegisterWebPush'))) throw new Error('network failed');
            return {};
        } };
        if (name === 'firebase/app') return { getApps: () => [], initializeApp: () => ({}) };
        if (name === 'firebase/messaging') return firebase;
        throw new Error('Unexpected module: ' + name);
    }, window: { rgWebPush: config, isSecureContext: true, PushManager: {}, Notification: {}, location: { origin: 'https://resgrid.test' } },
    navigator: { serviceWorker: { register: async () => worker, getRegistrations: async () => [worker] } }, Notification: { permission: 'granted' },
    localStorage: { getItem: k => storage.get(k), setItem: (k, value) => storage.set(k, value), removeItem: k => storage.delete(k) }, URL, Date, console });
    return { refresh: exports.refreshWebPush, calls, events, stored: () => JSON.parse(storage.get(key)) };
}
(async () => {
    const failed = load({ failRegister: true });
    await assert.rejects(failed.refresh(), /network failed/);
    assert.equal(failed.calls.length, 1);
    assert.equal(failed.stored().token, 'old', 'failed replacement keeps the old registration');
    for (const failUnregister of [false, true]) {
        const browser = load({ failUnregister });
        await browser.refresh();
        assert.deepEqual(browser.calls.map(call => call.url.split('/').at(-1)), ['RegisterDevice', 'UnRegisterWebPush']);
        assert.deepEqual(browser.calls.map(call => call.body.Token), ['new', 'old']);
        assert.equal(browser.stored().token, 'new');
    }
    const unchanged = load({ token: 'old' });
    await unchanged.refresh();
    assert.equal(unchanged.calls.length, 1);
    const switched = load({ userId: 'previous-user' });
    await switched.refresh();
    assert.deepEqual(switched.events, ['mint', 'delete', 'mint'], 'identity changes kill the previous FCM token first');
    assert.equal(switched.stored().userId, 'user');
    assert.equal(switched.calls.length, 1);
    console.log('ok - failed registration, rotation order, cleanup failure, unchanged tokens and identity changes');
})().catch(error => { console.error(error); process.exit(1); });
