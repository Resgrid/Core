(function () {
    'use strict';
    const translationNode = document.getElementById('checklist-translations');
    const translations = translationNode ? JSON.parse(translationNode.textContent) : {};
    const tr = value => translations[value] || value;
    const format = (key, ...values) => tr(key).replace(/\{(\d+)\}/g, (_, index) => values[Number(index)]);
    const types = ['Pass / Fail', 'Yes / No', 'Checkbox', 'Numeric reading', 'Quantity', 'Free text', 'Select list', 'Date', 'Photo', 'Signature'];
    const categories = ['Start of shift', 'Unit check', 'Personal gear', 'Annual review', 'Facility', 'Safety audit', 'Equipment check', 'Other'];
    const el = (tag, text, parent) => { const node = document.createElement(tag); if (text != null) node.textContent = text; if (parent) parent.appendChild(node); return node; };
    function button(parent, text, action) { const node = el('button', tr(text), parent); node.type = 'button'; node.className = 'btn btn-default btn-sm'; node.addEventListener('click', action); return node; }
    function field(parent, label, object, key, type, choices) {
        const box = el('div', null, parent); box.className = 'form-group';
        const caption = el('label', tr(label), box); const id = 'checklist-field-' + crypto.randomUUID(); caption.htmlFor = id;
        const input = el(choices ? 'select' : type === 'textarea' ? 'textarea' : 'input', null, box);
        input.id = id; input.className = type === 'checkbox' ? '' : 'form-control';
        if (choices) choices.forEach(choice => { const option = el('option', choice[2] === false ? choice[1] : tr(choice[1]), input); option.value = choice[0]; });
        else if (type !== 'textarea') input.type = type || 'text';
        if (type === 'number') input.step = 'any';
        if (type === 'checkbox') input.checked = !!object[key]; else input.value = object[key] == null ? '' : object[key];
        input.addEventListener('input', () => { object[key] = type === 'checkbox' ? input.checked : type === 'number' ? input.value === '' ? null : Number(input.value) : input.value; });
        return input;
    }
    function move(list, index, delta, render) { const next = index + delta; if (next < 0 || next >= list.length) return; [list[index], list[next]] = [list[next], list[index]]; render(); }
    function error(message) { const node = document.getElementById('checklist-error'); node.textContent = tr(message); node.hidden = false; node.scrollIntoView({ block: 'nearest' }); }
    async function post(form, url, data) {
        const headers = new Headers();
        if (window.resgridAdpReveal) window.resgridAdpReveal.applyGrantHeader(headers);
        const response = await fetch(url, { method: 'POST', body: data, headers, credentials: 'same-origin' });
        let result; try { result = await response.json(); } catch (_) { throw new Error('The request could not be completed. Your changes are still on this page.'); }
        if (!response.ok) {
            if (response.status === 403 && window.resgridAdpReveal) form.dispatchEvent(new CustomEvent('adp:grant-required'));
            throw new Error(result.message || result.Message || 'The request could not be completed.');
        }
        document.getElementById('checklist-error').hidden = true; return result;
    }
    function bindSave(form, collect) {
        form.addEventListener('submit', async event => {
            if (event.defaultPrevented) return;
            event.preventDefault();
            const submitter = event.submitter;
            if (form.dataset.busy === 'true') return;
            form.dataset.busy = 'true';
            try {
                collect(); const data = new FormData(form);
                if (submitter && submitter.name) data.set(submitter.name, submitter.value);
                const result = await post(form, form.action, data);
                if (result.url) { form.dispatchEvent(new CustomEvent("checklist:saved", { detail: result })); window.location.assign(result.url); return; }
                form.dispatchEvent(new CustomEvent('checklist:saved', { detail: result }));
            } catch (ex) { error(ex.message); } finally { form.dataset.busy = 'false'; }
        });
    }
    function editor() {
        const form = document.getElementById('checklist-editor'); if (!form) return;
        const model = JSON.parse(document.getElementById('checklist-form-data').textContent);
        const root = document.getElementById('checklist-builder');
        const freshItem = () => ({ Id: crypto.randomUUID(), Name: '', Type: 0, Required: true, Critical: false, AllowNotApplicable: false, RequireNoteOnFail: true, RequirePhotoOnFail: false, Weight: 1, PassingValue: 'true', Options: [] });
        function render() {
            root.replaceChildren();
            field(root, 'Checklist name', model, 'Name').maxLength = 200;
            field(root, 'Instructions (do not include patient data)', model, 'Instructions', 'textarea').maxLength = 10000;
            field(root, 'Category', model, 'Category', 'number', categories.map((name, i) => [i, name]));
            const targets = [[0, 'Department'], [1, 'Unit'], [2, 'Group / station'], [3, 'Personnel']];
            if (root.dataset.assetsAvailable === 'true' || model.TargetType === 5) targets.push([5, 'InventoryAsset']);
            field(root, 'Target type', model, 'TargetType', 'number', targets);
            const threshold = field(root, 'Passing score (%)', model, 'PassThreshold', 'number'); threshold.min = 0; threshold.max = 100;
            field(root, 'Require reported location', model, 'RequireLocation', 'checkbox');
            field(root, 'Require a different authenticated member to witness the submission', model, 'RequiresIndependentWitness', 'checkbox');
            const earlier = [];
            model.Sections.forEach((section, si) => {
                const sectionBox = el('fieldset', null, root); sectionBox.className = 'well'; el('legend', format('Section {0}', si + 1), sectionBox);
                field(sectionBox, 'Section name', section, 'Name').maxLength = 200;
                button(sectionBox, 'Move section up', () => move(model.Sections, si, -1, render));
                button(sectionBox, 'Move section down', () => move(model.Sections, si, 1, render));
                button(sectionBox, 'Remove section', () => { if (confirm(tr('Remove this section and its items from the draft?'))) { model.Sections.splice(si, 1); render(); } });
                section.Items.forEach((item, ii) => {
                    const box = el('fieldset', null, sectionBox); box.className = 'panel panel-default'; box.style.padding = '15px';
                    el('legend', format('Item {0}', ii + 1), box);
                    field(box, 'Question / check', item, 'Name').maxLength = 300;
                    field(box, 'Item instructions', item, 'Instructions', 'textarea').maxLength = 5000;
                    field(box, 'Answer type', item, 'Type', 'number', types.map((name, i) => [i, name])).addEventListener('change', render);
                    [['Required', 'Required'], ['Critical failure overrides the score', 'Critical'], ['Allow N/A with a reason', 'AllowNotApplicable'], ['Require a note on failure', 'RequireNoteOnFail'], ['Require a photo on failure', 'RequirePhotoOnFail']].forEach(pair => field(box, pair[0], item, pair[1], 'checkbox'));
                    const weight = field(box, 'Score weight (0 excludes this item from the score)', item, 'Weight', 'number'); weight.min = 0; weight.max = 1000;
                    if (item.Type === 1 || item.Type === 2) field(box, 'Passing answer', item, 'PassingValue', 'text', [['true', 'Yes / checked'], ['false', 'No / unchecked']]);
                    if (item.Type === 3 || item.Type === 4) {
                        field(box, 'Units', item, 'Units').maxLength = 50;
                        field(box, 'Minimum passing value (optional if maximum is set)', item, 'Minimum', 'number');
                        field(box, 'Maximum passing value (optional if minimum is set)', item, 'Maximum', 'number');
                    }
                    if (item.Type === 6) {
                        const options = { Lines: (item.Options || []).join('\n') };
                        field(box, 'Choices (one per line)', options, 'Lines', 'textarea').addEventListener('input', () => { item.Options = options.Lines.split('\n').map(value => value.trim()).filter(Boolean); });
                        field(box, 'Exact passing choice', item, 'PassingValue');
                    }
                    [['VisibleWhen', 'Show only when'], ['RequiredWhen', 'Also required when']].forEach(pair => {
                        if (item[pair[0]] && !earlier.some(source => source.Id === item[pair[0]].ItemId)) item[pair[0]] = null;
                        const value = { ItemId: item[pair[0]] ? item[pair[0]].ItemId : '' };
                        field(box, pair[1], value, 'ItemId', 'text', [['', 'Always / no condition']].concat(earlier.map(i => [i.Id, i.Name || tr('Unnamed earlier item'), false]))).addEventListener('change', () => {
                            item[pair[0]] = value.ItemId ? { ItemId: value.ItemId, EqualsValue: '' } : null; render();
                        });
                        if (item[pair[0]]) {
                            const source = earlier.find(candidate => candidate.Id === value.ItemId);
                            let choices;
                            if (source && source.Type === 0) choices = [['', 'Choose'], ['pass', 'Pass'], ['fail', 'Fail']];
                            if (source && (source.Type === 1 || source.Type === 2)) choices = [['', 'Choose'], ['true', source.Type === 1 ? 'Yes' : 'Checked'], ['false', source.Type === 1 ? 'No' : 'Unchecked']];
                            if (source && source.Type === 6) choices = [['', 'Choose']].concat((source.Options || []).map(option => [option, option, false]));
                            field(box, 'Answer that activates this condition', item[pair[0]], 'EqualsValue', source && source.Type === 7 ? 'date' : 'text', choices);
                        }
                    });
                    earlier.push(item);
                    button(box, 'Move item up', () => move(section.Items, ii, -1, render));
                    button(box, 'Move item down', () => move(section.Items, ii, 1, render));
                    button(box, 'Remove item', () => { if (confirm(tr('Remove this item from the draft?'))) { section.Items.splice(ii, 1); render(); } });
                });
                button(sectionBox, 'Add item', () => { section.Items.push(freshItem()); render(); });
            });
            button(root, 'Add section', () => { model.Sections.push({ Id: crypto.randomUUID(), Name: '', Items: [freshItem()] }); render(); });
        }
        render(); bindSave(form, () => { form.elements.formJson.value = JSON.stringify(model); });
    }
    function runner() {
        const form = document.getElementById('checklist-run'); if (!form) return;
        const model = JSON.parse(document.getElementById('checklist-run-data').textContent);
        const input = model.Input; const answers = new Map(input.Answers.map(answer => [answer.ItemId, answer]));
        const root = document.getElementById('checklist-answers'); const blocks = [];
        let files = model.Files;
        let dirty = false, generation = 0, savingGeneration = 0;
        const matches = condition => !condition || answers.has(condition.ItemId) && answers.get(condition.ItemId).Status === 1 && answers.get(condition.ItemId).Value === condition.EqualsValue;
        function visibility() {
            blocks.forEach(block => {
                const visible = matches(block.item.VisibleWhen); block.box.hidden = !visible;
                if (!visible) { block.answer.Status = 0; block.answer.Value = null; block.answer.Note = null; block.answer.NotApplicableReason = null; block.status.value = '0'; block.value.value = ''; }
                block.required.textContent = tr(block.item.Required || block.item.RequiredWhen && matches(block.item.RequiredWhen) ? 'Required' : 'Optional');
                if (block.answer.Status !== 1) { block.answer.Value = null; block.value.value = ""; } block.value.disabled = !visible || block.answer.Status !== 1;
                block.na.parentElement.hidden = block.answer.Status !== 2;
            });
        }
        function evidenceList(block) {
            block.evidence.replaceChildren();
            files.filter(file => (file.ItemId || file.itemId) === block.item.Id).forEach(file => {
                const id = file.Id || file.id;
                const line = el('p', null, block.evidence);
                const link = el('a', file.Content || file.content || tr('Evidence'), line); link.href = form.dataset.evidence + '?id=' + encodeURIComponent(id);
                if (window.resgridAdpReveal) link.addEventListener('click', event => { event.preventDefault(); window.resgridAdpReveal.download(link.href); });
                button(line, 'Remove', async () => {
                    if (form.dataset.busy === 'true') return;
                    form.dataset.busy = 'true';
                    try { const data = new FormData(form); data.set('id', id); data.set('completionId', model.Completion.Id); updateFiles(await post(form, form.dataset.remove, data)); }
                    catch (ex) { error(ex.message); } finally { form.dataset.busy = 'false'; }
                });
            });
        }
        function updateFiles(result) { input.Revision = result.revision; files = result.files; blocks.forEach(evidenceList); }
        async function upload(block, file) {
            if (!file || form.dataset.busy === 'true') return;
            if (file.size > 10 * 1024 * 1024) { error('Evidence must be at most 10 MB.'); return; }
            form.dataset.busy = 'true';
            try {
                const data = new FormData(form); data.set('itemId', block.item.Id); data.set('file', file, file.name || 'signature.png');
                updateFiles(await post(form, form.dataset.upload, data));
                if (block.item.Type === 8 || block.item.Type === 9) { block.answer.Status = 1; block.status.value = '1'; block.answer.Value = null; dirty = true; visibility(); }
            } catch (ex) { error(ex.message); } finally { form.dataset.busy = 'false'; }
        }
        model.Form.Sections.forEach(section => {
            el('h3', section.Name, root);
            section.Items.forEach(item => {
                const answer = answers.get(item.Id) || { ItemId: item.Id, Status: 0, Value: null, Note: null, NotApplicableReason: null }; answers.set(item.Id, answer);
                const box = el('fieldset', null, root); box.className = 'well'; el('legend', item.Name + (item.Critical ? ' — ' + tr('Critical') : ''), box);
                const required = el('strong', '', box); if (item.Instructions) el('p', item.Instructions, box);
                const states = [[0, 'Unanswered'], [1, 'Answered']]; if (item.AllowNotApplicable) states.push([2, 'N/A']);
                const status = field(box, 'Answer status', answer, 'Status', 'number', states);
                let choices, type = 'text';
                if (item.Type === 0) choices = [['', 'Choose'], ['pass', 'Pass'], ['fail', 'Fail']];
                if (item.Type === 1 || item.Type === 2) choices = [['', 'Choose'], ['true', item.Type === 1 ? 'Yes' : 'Checked'], ['false', item.Type === 1 ? 'No' : 'Unchecked']];
                if (item.Type === 6) choices = [['', 'Choose']].concat(item.Options.map(value => [value, value, false]));
                if (item.Type === 5) type = 'textarea'; if (item.Type === 7) type = 'date';
                const value = field(box, tr('Answer') + (item.Units ? ' (' + item.Units + ')' : ''), answer, 'Value', type, choices);
                if (item.Type === 3 || item.Type === 4) { value.inputMode = 'decimal'; el('p', format('Passing range: {0} to {1}', item.Minimum == null ? tr('No minimum') : item.Minimum, item.Maximum == null ? tr('No maximum') : item.Maximum), box); }
                if (item.Type === 8 || item.Type === 9) value.parentElement.hidden = true;
                const na = field(box, 'N/A reason', answer, 'NotApplicableReason', 'textarea'); na.maxLength = 2000;
                field(box, item.RequireNoteOnFail ? 'Note (required on failure)' : 'Note', answer, 'Note', 'textarea').maxLength = 5000;
                const evidence = el('div', null, box); const block = { box, item, answer, required, status, value, na, evidence }; blocks.push(block);
                const uploadLabel = el('label', tr('Evidence image (PNG/JPEG, up to 10 MB; scanning required)'), box);
                const picker = el('input', null, uploadLabel); picker.type = 'file'; picker.accept = 'image/png,image/jpeg';
                picker.addEventListener('change', async () => { try { await upload(block, picker.files[0]); } finally { picker.value = ''; } });
                if (item.Type === 9) {
                    el('p', tr('Draw your signature or upload a signature image. A required independent witness must sign in separately.'), box);
                    const canvas = el('canvas', null, box); canvas.width = 600; canvas.height = 160; canvas.style.cssText = 'max-width:100%;border:1px solid #777;touch-action:none;background:white';
                    canvas.setAttribute('aria-label', tr('Signature drawing area. Alternatively upload an image.'));
                    const context = canvas.getContext('2d'); context.fillStyle = 'white'; context.fillRect(0, 0, canvas.width, canvas.height); context.lineWidth = 2;
                    let drawing = false, marked = false;
                    const point = event => { const rect = canvas.getBoundingClientRect(); return [(event.clientX - rect.left) * canvas.width / rect.width, (event.clientY - rect.top) * canvas.height / rect.height]; };
                    canvas.addEventListener('pointerdown', event => { drawing = true; canvas.setPointerCapture(event.pointerId); context.beginPath(); context.moveTo(...point(event)); });
                    canvas.addEventListener('pointermove', event => { if (drawing) { context.lineTo(...point(event)); context.stroke(); marked = true; } });
                    canvas.addEventListener('pointerup', () => { drawing = false; }); canvas.addEventListener('pointercancel', () => { drawing = false; });
                    button(box, 'Clear signature', () => { context.fillRect(0, 0, canvas.width, canvas.height); marked = false; });
                    button(box, 'Save signature image', () => { if (!marked) { error('Draw a signature first.'); return; } canvas.toBlob(blob => upload(block, new File([blob], 'signature.png', { type: 'image/png' })), 'image/png'); });
                }
                evidenceList(block);
            });
        });
        field(root, 'Site / building / room', input, 'LocationDescription').maxLength = 500;
        field(root, 'Completion / handover note', input, 'Note', 'textarea').maxLength = 10000;
        const latitude = field(root, model.Form.RequireLocation ? 'Reported latitude (required)' : 'Reported latitude', input, 'Latitude', 'number');
        const longitude = field(root, 'Reported longitude', input, 'Longitude', 'number');
        button(root, 'Use current location', () => {
            if (!navigator.geolocation) { error('Location is unavailable. Enter coordinates manually.'); return; }
            navigator.geolocation.getCurrentPosition(position => { input.Latitude = position.coords.latitude; input.Longitude = position.coords.longitude; latitude.value = input.Latitude; longitude.value = input.Longitude; dirty = true; }, () => error('Location could not be read. Enter coordinates manually.'), { timeout: 15000, maximumAge: 0 });
        });
        root.addEventListener('input', () => { dirty = true; generation++; visibility(); }); visibility();
        bindSave(form, () => { savingGeneration = generation; visibility(); input.Answers = [...answers.values()]; form.elements.inputJson.value = JSON.stringify(input); });
        form.addEventListener('checklist:saved', event => { input.Revision = event.detail.revision; dirty = generation !== savingGeneration; const node = document.getElementById('checklist-saved'); node.textContent = tr(dirty ? 'Earlier progress saved; unsaved changes remain.' : 'Progress saved.'); node.hidden = false; });
        window.addEventListener('beforeunload', event => { if (dirty && form.dataset.busy !== 'true') { event.preventDefault(); event.returnValue = ''; } });
    }
    // The shared ADP binder installs its capture listener first and can hold a save for verification.
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', () => { editor(); runner(); }); else { editor(); runner(); }
}());
