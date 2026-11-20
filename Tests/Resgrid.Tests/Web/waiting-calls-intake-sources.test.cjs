// Pending calls list (resgrid.dispatch.waitingcalls.js): the intake source labels ("via SMS: Hospital line") are asked for in
// batches the endpoint accepts, only the ids that were sent are cached, and every cached answer expires.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.resolve(__dirname, '../../../Web/Resgrid.Web/wwwroot/js/app/internal/dispatch/resgrid.dispatch.waitingcalls.js'), 'utf8');

function load() {
    const requests = [];
    let clock = 1000000;
    function $(arg) {
        if (typeof arg === 'function') { return; }
        return { length: 0, ready: () => { }, text: () => { }, toggle: () => { }, on: () => { } };
    }
    $.map = (items, fn) => items.map(fn).filter(v => v !== null && v !== undefined);
    $.each = (items, fn) => { (Array.isArray(items) ? items : Object.keys(items)).forEach((v, i) => fn(i, v)); };
    $.ajax = options => {
        const request = { options, done: null, fail: null };
        requests.push(request);
        const promise = {
            done: fn => { request.done = fn; return promise; },
            fail: fn => { request.fail = fn; return promise; }
        };
        return promise;
    };
    const DateStub = { now: () => clock };
    const context = { $, resgrid: { absoluteBaseUrl: 'https://resgrid.test' }, Date: DateStub, Object, console, window: {}, document: {} };
    vm.runInNewContext(source, context);
    return {
        waiting: context.resgrid.dispatch.waitingcalls,
        requests,
        advance: ms => { clock += ms; }
    };
}

function rows(from, count) {
    return Array.from({ length: count }, (_, i) => ({ CallId: from + i }));
}

// 201 calls without a label: two requests (200 + 1), never 201 ids cached from a request that named 200.
{
    const page = load();
    page.waiting.loadIntakeSources(rows(1, 201));
    assert.equal(page.requests.length, 2);
    assert.equal(page.requests[0].options.data.callIds.split(',').length, 200);
    assert.equal(page.requests[1].options.data.callIds, '201');

    page.requests[0].done({ '5': 'via SMS: Hospital line' });
    assert.equal(page.waiting.intakeSourceText(5), 'via SMS: Hospital line');
    assert.equal(page.waiting.intakeSourceText(6), '');

    // The second batch answers on its own: call 201 gets its label.
    page.requests[1].done({ '201': 'via Webhook: CAD' });
    assert.equal(page.waiting.intakeSourceText(201), 'via Webhook: CAD');

    // A refresh while everything is cached asks for nothing.
    page.waiting.loadIntakeSources(rows(1, 201));
    assert.equal(page.requests.length, 2);
}

// In flight: a refresh does not ask again. Failed: the ids are asked for again next time.
{
    const page = load();
    page.waiting.loadIntakeSources(rows(1, 3));
    page.waiting.loadIntakeSources(rows(1, 3));
    assert.equal(page.requests.length, 1, 'no second request while the first is in flight');

    page.requests[0].fail();
    page.waiting.loadIntakeSources(rows(1, 3));
    assert.equal(page.requests.length, 2, 'a failed request is retried on the next refresh');
}

// Every answer expires: a call with no label is asked again after a couple of minutes, a label after half an hour.
{
    const page = load();
    page.waiting.loadIntakeSources(rows(1, 2));
    page.requests[0].done({ '1': 'via Email: Hospital' });

    page.advance(60 * 1000);
    page.waiting.loadIntakeSources(rows(1, 2));
    assert.equal(page.requests.length, 1, 'still fresh after a minute');

    page.advance(2 * 60 * 1000);
    page.waiting.loadIntakeSources(rows(1, 2));
    assert.equal(page.requests.length, 2);
    assert.equal(page.requests[1].options.data.callIds, '2', 'only the call that had no label is asked again');

    page.advance(30 * 60 * 1000);
    assert.equal(page.waiting.intakeSourceText(1), '', 'an expired label is not shown');
}

console.log('waiting-calls-intake-sources: ok');
