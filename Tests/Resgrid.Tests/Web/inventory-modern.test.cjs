const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

test('report date bounds are omitted for snapshots and preserved when returning to history', async () => {
    const form = new Element('form');
    const kind = form.appendChild(new Element('select', { name: 'Kind', value: '0' }));
    const from = form.appendChild(new Element('input', { name: 'FromUtc', value: '2026-09-01' }));
    const until = form.appendChild(new Element('input', { name: 'UntilUtc', value: '2026-09-09' }));
    const script = fs.readFileSync(path.resolve(__dirname, '../../../Web/Resgrid.Web/wwwroot/js/app/internal/inventory/inventory-operations.js'), 'utf8');
    vm.runInNewContext(script, { document: { getElementById: () => form } });
    for (const value of ['0', '3', '6']) { kind.value = value; await kind.emit('change'); assert.equal(from.disabled, true); assert.equal(until.disabled, true); }
    for (const value of ['1', '2', '4', '5', '7']) { kind.value = value; await kind.emit('change'); assert.equal(from.disabled, false); assert.equal(until.disabled, false); }
    assert.equal(from.value, '2026-09-01'); assert.equal(until.value, '2026-09-09');
});

test('report recipient filters select either a person or a unit without sending a stale selection', async () => {
    const form = new Element('form');
    form.appendChild(new Element('select', { name: 'Kind', value: '5' }));
    const person = form.appendChild(new Element('select', { name: 'UserId', value: '' }));
    const unit = form.appendChild(new Element('select', { name: 'UnitId', value: '7' }));
    const script = fs.readFileSync(path.resolve(__dirname, '../../../Web/Resgrid.Web/wwwroot/js/app/internal/inventory/inventory-operations.js'), 'utf8');
    vm.runInNewContext(script, { document: { getElementById: () => form } });
    person.value = 'member'; await person.emit('change'); assert.equal(unit.value, '');
    assert.equal(person.value, 'member');
    unit.value = '9'; await unit.emit('change'); assert.equal(person.value, ''); assert.equal(unit.value, '9');
    unit.value = ''; await unit.emit('change'); assert.equal(person.value, '');
});

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
        for (const key of ['id', 'name', 'value', 'type', 'className', 'htmlFor', 'disabled', 'readOnly']) copy[key] = this[key];
        if (deep) this.children.forEach(child => copy.appendChild(child.cloneNode(true)));
        return copy;
    }
    submit() { this.submissions.push(Object.fromEntries(new FormData(this))); }
    replaceChildren() { this.children = []; }
}
class FormData {
    constructor(form) { this.values = form.querySelectorAll('[name]').filter(field => !field.disabled).map(field => [field.name, field.value]); }
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
function page({ response = { ok: true, json: async () => ({ success: true }) }, createAsset = false, denied = false, kit = false, issuedAssets = null, movementType = null } = {}) {
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
    let movement, cost;
    if (movementType !== null) {
        movement = field(command, 'Lines[0].Type', movementType, 'select');
        cost = field(command, 'Lines[0].UnitCost', '19.500001'); cost.type = 'number';
    }
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
    return { settings, message, reopen, getForm, command, requests, bound, lines, movement, cost };
}

function purchaseLine(formIndex, index, { receipt, serialized }) {
    const row = new Element('fieldset', { className: 'm4-line' });
    field(row, 'Lines[' + index + '].' + (receipt ? 'PurchaseOrderItemId' : 'Id'), 'order-line-' + formIndex + '-' + index);
    const controls = receipt
        ? [['Quantity', serialized ? '1' : '4.125001', 'number'], ['LocationId', 'location-' + index, 'select'], ['LotId', 'lot-' + index, 'select']]
        : [['ItemId', 'item-' + index, 'select'], ['QuantityOrdered', '2.125001', 'number'], ['UnitCost', '19.500001', 'number'], ['Note', 'draft note', 'text']];
    if (receipt && serialized) controls.push(['Asset.SerialNumber', 'serial-' + index, 'text'], ['Asset.AssetTag', 'tag-' + index, 'text'], ['Asset.Barcode', 'barcode-' + index, 'text'], ['Asset.ExpiresOn', '2030-01-01', 'date']);
    for (const [name, value, type] of controls) {
        const id = 'purchase-' + formIndex + '-' + index + '-' + name.replaceAll('.', '-');
        row.appendChild(new Element('label', { htmlFor: id }));
        const input = field(row, 'Lines[' + index + '].' + name, value, type === 'select' ? 'select' : 'input');
        input.id = id; input.type = type; input.readOnly = !!(receipt && serialized && name === 'Quantity');
    }
    if (receipt) row.appendChild(new Element('button', { className: 'm4-copy-line' }));
    row.appendChild(new Element('button', { className: 'm4-remove-line' }));
    return row;
}
function purchasingPage(specifications = [{ receipt: false, count: 2 }]) {
    const document = new Element('document');
    const forms = specifications.map(({ receipt = false, serialized = true, count = 2 }, formIndex) => {
        const form = document.appendChild(new Element('form', { className: 'm4-lines-form', dataset: { receipt: String(receipt) } }));
        const lines = form.appendChild(new Element('div', { className: 'm4-lines' }));
        for (let index = 0; index < count; index++) lines.appendChild(purchaseLine(formIndex, index, { receipt, serialized }));
        const add = receipt ? null : form.appendChild(new Element('button', { className: 'm4-add-line' }));
        return { form, lines, add };
    });
    vm.runInNewContext(fs.readFileSync(path.resolve(__dirname, '../../../Web/Resgrid.Web/wwwroot/js/app/internal/inventory/inventory-purchasing.js'), 'utf8'), { document });
    return forms;
}
function assertPurchaseBindings(forms) {
    for (const { lines } of forms) lines.children.forEach((row, index) => {
        assert.ok(row.querySelectorAll('[name]').every(input => input.name.startsWith('Lines[' + index + '].')));
        const ids = row.querySelectorAll('[id]').map(input => input.id);
        assert.deepEqual(row.querySelectorAll('label[for]').map(label => label.htmlFor), ids);
    });
    const ids = forms.flatMap(({ lines }) => lines.querySelectorAll('[id]').map(input => input.id));
    assert.equal(new Set(ids).size, ids.length, 'visible controls have unique IDs across purchasing forms');
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

test('purchase draft addition clears the stored line identity and starts a new editable line', async () => {
    const forms = purchasingPage(); const [ui] = forms;
    await ui.form.emit('click', ui.add);
    assert.equal(ui.lines.children.length, 3);
    assert.deepEqual(Object.fromEntries(new FormData(ui.lines.children[2])), {
        'Lines[2].Id': '', 'Lines[2].ItemId': '', 'Lines[2].QuantityOrdered': '1', 'Lines[2].UnitCost': '0', 'Lines[2].Note': ''
    });
    assert.equal(ui.lines.children[0].querySelector('[name="Lines[0].Id"]').value, 'order-line-0-0');
    assert.equal(ui.lines.children[0].querySelector('[name="Lines[0].UnitCost"]').value, '19.500001');
    await ui.form.emit('click', ui.lines.children[0].querySelector('.m4-remove-line'));
    assert.equal(ui.lines.children[0].querySelector('[name="Lines[0].Id"]').value, 'order-line-0-1');
    assert.equal(ui.lines.children[1].querySelector('[name="Lines[1].Id"]').value, '');
    assertPurchaseBindings(forms);
});

test('serialized receipt copies keep order line, location and lot while clearing new asset identifiers', async () => {
    const forms = purchasingPage([{ receipt: true, count: 2 }]); const [ui] = forms;
    await ui.form.emit('click', ui.lines.children[1].querySelector('.m4-copy-line'));
    const copied = ui.lines.children[2];
    assert.deepEqual(Object.fromEntries(new FormData(copied)), {
        'Lines[2].PurchaseOrderItemId': 'order-line-0-1', 'Lines[2].Quantity': '1', 'Lines[2].LocationId': 'location-1', 'Lines[2].LotId': 'lot-1',
        'Lines[2].Asset.SerialNumber': '', 'Lines[2].Asset.AssetTag': '', 'Lines[2].Asset.Barcode': '', 'Lines[2].Asset.ExpiresOn': ''
    });
    assert.equal(copied.querySelector('[name="Lines[2].Quantity"]').readOnly, true);
    assert.equal(ui.lines.children[1].querySelector('[name="Lines[1].Asset.SerialNumber"]').value, 'serial-1');
    await ui.form.emit('click', ui.lines.children[1].querySelector('.m4-remove-line'));
    assert.equal(ui.lines.children[1].querySelector('[name="Lines[1].PurchaseOrderItemId"]').value, 'order-line-0-1');
    assertPurchaseBindings(forms);
});

test('bulk receipt copies start at one and keep their selected destination and lot', async () => {
    const forms = purchasingPage([{ receipt: true, serialized: false, count: 1 }]); const [ui] = forms;
    await ui.form.emit('click', ui.lines.children[0].querySelector('.m4-copy-line'));
    assert.equal(ui.lines.children[0].querySelector('[name="Lines[0].Quantity"]').value, '4.125001');
    assert.deepEqual(Object.fromEntries(new FormData(ui.lines.children[1])), {
        'Lines[1].PurchaseOrderItemId': 'order-line-0-0', 'Lines[1].Quantity': '1', 'Lines[1].LocationId': 'location-0', 'Lines[1].LotId': 'lot-0'
    });
    assertPurchaseBindings(forms);
});

test('purchasing forms enforce one to one hundred rows and retain unique accessible IDs after removals', async () => {
    const forms = purchasingPage([{ receipt: false, count: 1 }, { receipt: true, count: 1 }]);
    for (const ui of forms) {
        const add = () => ui.add || ui.lines.children[0].querySelector('.m4-copy-line');
        assert.equal(ui.lines.children[0].querySelector('.m4-remove-line').disabled, true);
        await ui.form.emit('click', ui.lines.children[0].querySelector('.m4-remove-line'));
        assert.equal(ui.lines.children.length, 1);
        for (let index = 1; index < 100; index++) await ui.form.emit('click', add());
        assert.equal(ui.lines.children.length, 100);
        assert.ok(ui.form.querySelectorAll('.m4-add-line, .m4-copy-line').every(button => button.disabled));
        await ui.form.emit('click', add()); assert.equal(ui.lines.children.length, 100);
        await ui.form.emit('click', ui.lines.children[49].querySelector('.m4-remove-line'));
        assert.equal(ui.lines.children.length, 99);
        assert.ok(ui.form.querySelectorAll('.m4-add-line, .m4-copy-line').every(button => !button.disabled));
        await ui.form.emit('click', add()); assert.equal(ui.lines.children.length, 100);
    }
    assertPurchaseBindings(forms);
});

test('optional movement cost is submitted only for Receive and survives switching movement types', async () => {
    const ui = page({ movementType: '3' });
    assert.equal(ui.cost.disabled, true);
    await ui.command.emit('submit');
    assert.equal(Object.hasOwn(ui.requests[0].values, 'Lines[0].UnitCost'), false);
    ui.movement.value = '1'; await ui.movement.emit('change');
    assert.equal(ui.cost.disabled, false);
    await ui.command.emit('submit'); assert.equal(ui.requests[1].values['Lines[0].UnitCost'], '19.500001');
    ui.movement.value = '2'; await ui.movement.emit('change');
    assert.equal(ui.cost.disabled, true); assert.equal(ui.cost.value, '19.500001');
    await ui.command.emit('submit'); assert.equal(Object.hasOwn(ui.requests[2].values, 'Lines[0].UnitCost'), false);
    assert.equal(page({ movementType: '1' }).cost.disabled, false);
});
