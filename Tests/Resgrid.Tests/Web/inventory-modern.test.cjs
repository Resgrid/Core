const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

// A deliberately small DOM adapter executes the shipped script without a browser/package dependency.
// It models only selectors and native form operations used by the inventory page.
class Element {
    constructor(tag, properties = {}) {
        this.tagName = tag.toLowerCase(); this.children = []; this.parentElement = null;
        this.dataset = {}; this.listeners = {}; this.className = ''; this.id = ''; this.name = '';
        this.value = ''; this.type = ''; this.method = 'post'; this.textContent = ''; this.hidden = false;
        this.submissions = []; Object.assign(this, properties);
        this.classList = { contains: name => this.className.split(/\s+/).includes(name) };
    }
    appendChild(child) { child.parentElement = this; this.children.push(child); return child; }
    get firstElementChild() { return this.children[0]; }
    matches(selector) {
        const attribute = selector.match(/\[([^\]$=]+)(\$?=)?(?:"([^"]*)")?\]/);
        const head = attribute ? selector.replace(attribute[0], '') : selector;
        const [tag, className] = head.split('.');
        if (tag && tag !== this.tagName) return false;
        if (className && !this.classList.contains(className)) return false;
        if (!attribute) return true;
        const value = attribute[1] === 'for' ? this.htmlFor : this[attribute[1]];
        return attribute[2] === '$=' ? String(value || '').endsWith(attribute[3]) : attribute[2] === '=' ? value === attribute[3] : !!value;
    }
    querySelectorAll(selector) {
        const matches = [], selectors = selector.split(',').map(value => value.trim());
        for (const child of this.children) {
            if (selectors.some(value => child.matches(value))) matches.push(child);
            matches.push(...child.querySelectorAll(selector));
        }
        return matches;
    }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
    closest(selector) { return this.matches(selector) ? this : this.parentElement?.closest(selector) || null; }
    addEventListener(name, listener) { (this.listeners[name] ||= []).push(listener); }
    async emit(name, target = this) {
        const event = { target, defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
        for (const listener of this.listeners[name] || []) await listener(event);
    }
    remove() { this.parentElement.children.splice(this.parentElement.children.indexOf(this), 1); this.parentElement = null; }
    cloneNode(deep) {
        const copy = new Element(this.tagName);
        for (const key of ['id', 'name', 'value', 'type', 'className', 'htmlFor', 'disabled']) copy[key] = this[key];
        if (deep) this.children.forEach(child => copy.appendChild(child.cloneNode(true)));
        return copy;
    }
    submit() { this.submissions.push(Object.fromEntries(new FormData(this))); }
    replaceChildren() { this.children = []; }
}
class FormData {
    constructor(form) { this.values = form.querySelectorAll('[name]').map(field => [field.name, field.value]); }
    [Symbol.iterator]() { return this.values[Symbol.iterator](); }
}
const field = (form, name, value, tag = 'input') => form.appendChild(new Element(tag, { name, value, type: 'hidden' }));
function kitLine(index, item) {
    const row = new Element('div', { className: 'inventory-kit-line' });
    for (const [name, value, type] of [['ItemId', item, 'text'], ['Quantity', '1', 'number']]) {
        const id = 'kit-' + index + '-' + name;
        row.appendChild(new Element('label', { htmlFor: id }));
        field(row, 'Lines[' + index + '].' + name, value, name === 'ItemId' ? 'select' : 'input').id = id;
        row.children.at(-1).type = type;
    }
    row.appendChild(new Element('button', { className: 'inventory-remove-line' }));
    return row;
}
function page({ response = { ok: true, json: async () => ({ success: true }) }, createAsset = false, denied = false, kit = false, issuedAssets = null } = {}) {
    const document = new Element('document');
    document.createElement = tag => new Element(tag);
    document.getElementById = id => document.querySelectorAll('[id]').find(element => element.id === id);
    const settings = document.appendChild(new Element('script', { id: 'inventory-settings', textContent: JSON.stringify({ protectedData: true, grant: 'synthetic-grant', expiry: '2030-01-01T00:00:00Z', index: '/User/Inventory', failed: 'Unable to complete', witness: 'Awaiting witness' }) }));
    const message = document.appendChild(new Element('p', { id: 'inventory-message', hidden: true }));
    const reopen = document.appendChild(new Element('form', { id: 'inventory-page', className: 'inventory-navigation' }));
    field(reopen, 'itemId', 'item-filter'); field(reopen, 'locationId', 'location-filter');
    const getForm = document.appendChild(new Element('form', { method: 'get', className: 'inventory-navigation' }));
    const command = document.appendChild(new Element('form', { action: '/User/Inventory/Post', className: 'inventory-command' + (kit ? ' inventory-kit' : '') + (issuedAssets ? ' inventory-kit-issue' : '') }));
    field(command, '__RequestVerificationToken', 'synthetic-csrf'); field(command, 'RequestId', 'synthetic-request'); field(command, 'Note', 'unsaved note');
    if (createAsset) command.dataset.inventoryCreateAsset = 'true';
    if (issuedAssets) issuedAssets.forEach((id, index) => field(command, 'Lines[' + index + '].AssetId', id, 'select'));
    let lines;
    if (kit) {
        lines = command.appendChild(new Element('div', { className: 'inventory-kit-lines' })); lines.appendChild(kitLine(0, 'item-a')); lines.appendChild(kitLine(1, 'item-b'));
        command.appendChild(new Element('button', { className: 'inventory-add-line' }));
    }
    const requests = [], bound = [];
    const window = { resgridAdpReveal: { bindForm(form) { bound.push(form); form.addEventListener('submit', event => { if (denied) event.preventDefault(); }); } } };
    vm.runInNewContext(fs.readFileSync(path.resolve(__dirname, '../../../Web/Resgrid.Web/wwwroot/js/app/internal/inventory/inventory-modern.js'), 'utf8'), {
        document, window, FormData, $: callback => callback(), fetch: async (url, options) => { requests.push({ url, ...options, values: Object.fromEntries(options.body) }); return typeof response === 'function' ? response() : response; }
    });
    return { settings, message, reopen, getForm, command, requests, bound, lines };
}

test('grant and expiry reach POST forms only, then leave the bootstrap JSON', () => {
    const ui = page();
    assert.equal(ui.command.querySelector('[name="__ResgridProtectedGrant"]').value, 'synthetic-grant');
    assert.equal(ui.command.querySelector('[name="__ResgridProtectedGrantExpiresOn"]').value, '2030-01-01T00:00:00Z');
    assert.equal(ui.getForm.querySelector('[name="__ResgridProtectedGrant"]'), null);
    assert.equal(ui.bound.includes(ui.getForm), false);
    assert.equal(ui.settings.textContent, '');
});
test('ADP interception prevents the command from reaching the server', async () => {
    const ui = page({ denied: true }); await ui.command.emit('submit');
    assert.equal(ui.requests.length, 0); assert.equal(ui.reopen.submissions.length, 0);
});
test('success reopens by POST retaining grant, expiry and structural filters', async () => {
    const ui = page(); await ui.command.emit('submit');
    assert.equal(ui.requests[0].method, 'POST'); assert.equal(ui.requests[0].cache, 'no-store');
    assert.equal(ui.requests[0].values.__RequestVerificationToken, 'synthetic-csrf');
    assert.equal(ui.reopen.submissions.length, 1);
    assert.deepEqual(ui.reopen.submissions[0], { itemId: 'item-filter', locationId: 'location-filter', __ResgridProtectedGrant: 'synthetic-grant', __ResgridProtectedGrantExpiresOn: '2030-01-01T00:00:00Z' });
});
test('failed commands retain entered values and do not reload', async () => {
    const ui = page({ response: { ok: false, json: async () => ({ message: 'Revision conflict' }) } }); await ui.command.emit('submit');
    assert.equal(ui.message.textContent, 'Revision conflict'); assert.equal(ui.message.hidden, false);
    assert.equal(ui.command.querySelector('[name="Note"]').value, 'unsaved note'); assert.equal(ui.reopen.submissions.length, 0);
});
test('controlled asset receive and ordinary pending commands display their stable witness request', async () => {
    for (const options of [{ createAsset: true, response: { ok: true, json: async () => ({ Id: 'asset-id', CurrentLocationId: null }) } }, { response: { ok: true, json: async () => ({ awaitingWitness: true }) } }]) {
        const ui = page(options); await ui.command.emit('submit');
        assert.equal(ui.message.textContent, 'Awaiting witness synthetic-request'); assert.equal(ui.reopen.submissions.length, 0);
    }
});
test('an outstanding command suppresses double submission and waits for its receipt', async () => {
    let complete; const pending = new Promise(resolve => { complete = resolve; });
    const ui = page({ response: () => pending });
    const first = ui.command.emit('submit'); await Promise.resolve(); await Promise.resolve();
    await ui.command.emit('submit');
    assert.equal(ui.requests.length, 1); assert.equal(ui.reopen.submissions.length, 0);
    complete({ ok: true, json: async () => ({ success: true }) }); await first;
    assert.equal(ui.reopen.submissions.length, 1);
});
test('serialized kits reject missing or duplicate asset selections before posting', async () => {
    for (const issuedAssets of [['asset-a', 'asset-a'], ['asset-a', '']]) {
        const ui = page({ issuedAssets }); await ui.command.emit('submit'); assert.equal(ui.requests.length, 0); assert.equal(ui.message.hidden, false);
    }
    const valid = page({ issuedAssets: ['asset-a', 'asset-b'] }); await valid.command.emit('submit'); assert.equal(valid.requests.length, 1);
});
test('kit removal and addition keep contiguous binding indices and unique accessible IDs', async () => {
    const ui = page({ kit: true });
    await ui.command.emit('click', ui.lines.children[0].querySelector('.inventory-remove-line'));
    assert.equal(ui.lines.children[0].querySelector('[name="Lines[0].ItemId"]').value, 'item-b');
    assert.equal(ui.lines.children[0].querySelector('.inventory-remove-line').disabled, true);
    await ui.command.emit('click', ui.command.querySelector('.inventory-add-line'));
    await ui.command.emit('click', ui.command.querySelector('.inventory-add-line'));
    assert.deepEqual(ui.lines.querySelectorAll('[name]').map(input => input.name), ['Lines[0].ItemId', 'Lines[0].Quantity', 'Lines[1].ItemId', 'Lines[1].Quantity', 'Lines[2].ItemId', 'Lines[2].Quantity']);
    const ids = ui.lines.querySelectorAll('[name]').map(input => input.id); assert.equal(new Set(ids).size, ids.length);
    assert.deepEqual(ui.lines.querySelectorAll('label[for]').map(label => label.htmlFor), ids);
    assert.equal(ui.lines.children[1].querySelector('[name="Lines[1].Quantity"]').value, '1');
    assert.equal(ui.lines.children[1].querySelector('[name="Lines[1].ItemId"]').value, '');
});
