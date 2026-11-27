var resgrid;
(function (resgrid) {
    var protocols;
    (function (protocols) {
        var blocks;
        (function (blocks) {
            // The block editor (Protocols P3). The document is the state: every control writes straight into its block object,
            // structural changes (add, remove, move) redraw the list, and the document is posted as JSON (ContentJson) on save,
            // on autosave (every 30 seconds while it changed) and to the preview, which the server renders exactly as members read it.
            // Every control is a button, input or editable region, so the whole editor works from the keyboard; blocks move with
            // Move up / Move down rather than by dragging. Rich text regions are sanitized again by the server on every save.
            var state = { doc: { schema: 2, blocks: [] }, data: {}, text: {}, dirty: false };

            function t(key) {
                return (state.text && state.text[key]) || key;
            }

            function newKey() {
                var chars = 'abcdefghijklmnopqrstuvwxyz0123456789';
                var key = '';
                for (var i = 0; i < 8; i++) {
                    key += chars.charAt(Math.floor(Math.random() * chars.length));
                }
                return key;
            }

            function markDirty() {
                state.dirty = true;
            }

            function el(tag, className, text) {
                var node = document.createElement(tag);
                if (className) {
                    node.className = className;
                }
                if (text !== undefined && text !== null) {
                    node.textContent = text;
                }
                return node;
            }

            function iconButton(icon, label, css, onClick) {
                var button = el('button', 'btn btn-xs ' + (css || 'btn-default'));
                button.type = 'button';
                var i = el('i', 'fa ' + icon);
                i.setAttribute('aria-hidden', 'true');
                button.appendChild(i);
                button.appendChild(document.createTextNode(' ' + label));
                button.addEventListener('click', onClick);
                return button;
            }

            function field(labelText, control, id) {
                var group = el('div', 'form-group');
                var label = el('label', 'control-label', labelText);
                if (id) {
                    label.setAttribute('for', id);
                    control.id = id;
                }
                group.appendChild(label);
                group.appendChild(control);
                return group;
            }

            function input(value, onChange, type, maxLength) {
                var box = el('input', 'form-control');
                box.type = type || 'text';
                box.value = value || '';
                if (maxLength) {
                    box.maxLength = maxLength;
                }
                box.addEventListener('input', function () {
                    onChange(box.value);
                    markDirty();
                });
                return box;
            }

            function select(options, value, onChange) {
                var box = el('select', 'form-control');
                options.forEach(function (option) {
                    var o = el('option', null, option.text);
                    o.value = option.value;
                    if (String(option.value) === String(value === undefined || value === null ? '' : value)) {
                        o.selected = true;
                    }
                    box.appendChild(o);
                });
                box.addEventListener('change', function () {
                    onChange(box.value);
                    markDirty();
                });
                return box;
            }

            // A rich text region: the stored (server-sanitized) markup is shown as is; bold, italic and lists from the small
            // toolbar or the usual shortcuts. The server sanitizes whatever comes back.
            function rich(value, onChange, labelText, id) {
                var wrap = el('div', 'form-group rgp-rich');
                var label = el('span', 'control-label', labelText);
                label.id = id + '-label';
                wrap.appendChild(label);
                var bar = el('div', 'rgp-rich-toolbar');
                bar.setAttribute('role', 'toolbar');
                bar.setAttribute('aria-label', labelText);
                var area = el('div', 'form-control rgp-rich-area');
                area.id = id;
                area.setAttribute('contenteditable', 'true');
                area.setAttribute('role', 'textbox');
                area.setAttribute('aria-multiline', 'true');
                area.setAttribute('aria-labelledby', id + '-label');
                area.innerHTML = value || '';
                [['bold', 'fa-bold', 'RichBold'], ['italic', 'fa-italic', 'RichItalic'], ['insertUnorderedList', 'fa-list-ul', 'RichBullets'], ['insertOrderedList', 'fa-list-ol', 'RichNumbers']].forEach(function (command) {
                    bar.appendChild(iconButton(command[1], t(command[2]), 'btn-white', function () {
                        area.focus();
                        document.execCommand(command[0], false, null);
                        onChange(area.innerHTML);
                        markDirty();
                    }));
                });
                area.addEventListener('input', function () {
                    onChange(area.innerHTML);
                    markDirty();
                });
                wrap.appendChild(bar);
                wrap.appendChild(area);
                return wrap;
            }

            function scopes(block, id) {
                var list = state.data.certificationTypes || [];
                if (!list.length) {
                    return null;
                }
                var box = el('fieldset', 'rgp-scopes');
                box.appendChild(el('legend', 'control-label', t('BlockFieldScopes')));
                list.forEach(function (type, index) {
                    var label = el('label', 'checkbox-inline');
                    var check = document.createElement('input');
                    check.type = 'checkbox';
                    check.id = id + '-scope-' + index;
                    check.checked = (block.scopes || []).some(function (s) { return s.id === type.id; });
                    check.addEventListener('change', function () {
                        var current = (block.scopes || []).filter(function (s) { return s.id !== type.id; });
                        if (check.checked) {
                            current.push({ id: type.id, name: type.name });
                        }
                        block.scopes = current.length ? current : undefined;
                        markDirty();
                    });
                    label.appendChild(check);
                    label.appendChild(document.createTextNode(' ' + type.name));
                    box.appendChild(label);
                });
                return box;
            }

            function typeName(type) {
                return t('BlockType' + type.charAt(0).toUpperCase() + type.slice(1));
            }

            function blankBlock(type) {
                var block = { type: type, key: newKey() };
                if (type === 'heading') { block.text = ''; block.level = 2; }
                if (type === 'steps') { block.items = [{ key: newKey(), html: '' }]; }
                if (type === 'table') { block.rows = [['', ''], ['', '']]; block.headerRow = true; }
                if (type === 'link') { block.linkKind = 'protocol'; }
                if (type === 'medication') { block.medication = { drug: '' }; }
                if (type === 'variants') {
                    block.variants = [
                        { key: newKey(), label: t('BlockAdultLabel'), blocks: [] },
                        { key: newKey(), label: t('BlockPediatricLabel'), blocks: [] }
                    ];
                }
                return block;
            }

            function moveIn(list, index, delta) {
                var target = index + delta;
                if (target < 0 || target >= list.length) {
                    return false;
                }
                var item = list.splice(index, 1)[0];
                list.splice(target, 0, item);
                markDirty();
                return true;
            }

            function renderFields(block, body, id) {
                var med;
                switch (block.type) {
                    case 'heading':
                        body.appendChild(field(t('BlockFieldText'), input(block.text, function (v) { block.text = v; }, 'text', 200), id + '-text'));
                        body.appendChild(field(t('BlockFieldLevel'), select([
                            { value: 2, text: t('BlockLevel2') }, { value: 3, text: t('BlockLevel3') }, { value: 4, text: t('BlockLevel4') }
                        ], block.level || 2, function (v) { block.level = Number(v); }), id + '-level'));
                        break;
                    case 'paragraph':
                    case 'html':
                        body.appendChild(rich(block.html, function (v) { block.html = v; }, t('BlockFieldBody'), id + '-html'));
                        break;
                    case 'steps':
                        body.appendChild(field(t('BlockFieldTitle'), input(block.title, function (v) { block.title = v; }, 'text', 200), id + '-title'));
                        var list = el('ol', 'rgp-editor-steps');
                        (block.items || []).forEach(function (item, index) {
                            var li = el('li');
                            li.appendChild(rich(item.html, function (v) { item.html = v; }, t('BlockFieldStep') + ' ' + (index + 1), id + '-step-' + item.key));
                            var actions = el('div', 'rgw-row-actions');
                            actions.appendChild(iconButton('fa-arrow-up', t('BlockMoveUp'), 'btn-white', function () { if (moveIn(block.items, index, -1)) { redraw(); } }));
                            actions.appendChild(iconButton('fa-arrow-down', t('BlockMoveDown'), 'btn-white', function () { if (moveIn(block.items, index, 1)) { redraw(); } }));
                            actions.appendChild(iconButton('fa-times', t('BlockRemoveStep'), 'btn-white', function () { block.items.splice(index, 1); markDirty(); redraw(); }));
                            li.appendChild(actions);
                            list.appendChild(li);
                        });
                        body.appendChild(list);
                        body.appendChild(iconButton('fa-plus', t('BlockAddStep'), 'btn-default', function () {
                            block.items = block.items || [];
                            block.items.push({ key: newKey(), html: '' });
                            markDirty();
                            redraw();
                        }));
                        break;
                    case 'caution':
                    case 'warning':
                        body.appendChild(field(t('BlockFieldTitle'), input(block.title, function (v) { block.title = v; }, 'text', 200), id + '-title'));
                        body.appendChild(rich(block.html, function (v) { block.html = v; }, t('BlockFieldBody'), id + '-html'));
                        break;
                    case 'gate':
                        body.appendChild(rich(block.html, function (v) { block.html = v; }, t('BlockFieldBody'), id + '-html'));
                        body.appendChild(field(t('BlockFieldContact'), input(block.contact, function (v) { block.contact = v; }, 'text', 200), id + '-contact'));
                        break;
                    case 'table':
                        body.appendChild(field(t('BlockFieldCaption'), input(block.title, function (v) { block.title = v; }, 'text', 200), id + '-caption'));
                        var header = el('label', 'checkbox-inline');
                        var check = document.createElement('input');
                        check.type = 'checkbox';
                        check.checked = block.headerRow === true;
                        check.addEventListener('change', function () { block.headerRow = check.checked; markDirty(); });
                        header.appendChild(check);
                        header.appendChild(document.createTextNode(' ' + t('BlockFieldHeaderRow')));
                        body.appendChild(header);
                        var grid = el('div', 'table-responsive');
                        var table = el('table', 'table table-bordered rgp-editor-grid');
                        var rows = block.rows = block.rows || [['']];
                        rows.forEach(function (row, r) {
                            var tr = el('tr');
                            row.forEach(function (cell, c) {
                                var td = el('td');
                                var box = input(cell, function (v) { row[c] = v; }, 'text', 500);
                                box.setAttribute('aria-label', t('BlockFieldCell') + ' ' + (r + 1) + '.' + (c + 1));
                                td.appendChild(box);
                                tr.appendChild(td);
                            });
                            table.appendChild(tr);
                        });
                        grid.appendChild(table);
                        body.appendChild(grid);
                        var tableActions = el('div', 'rgw-row-actions');
                        tableActions.appendChild(iconButton('fa-plus', t('BlockAddRow'), 'btn-default', function () {
                            rows.push(new Array((rows[0] || ['']).length).fill(''));
                            markDirty();
                            redraw();
                        }));
                        tableActions.appendChild(iconButton('fa-plus', t('BlockAddColumn'), 'btn-default', function () {
                            rows.forEach(function (row) { row.push(''); });
                            markDirty();
                            redraw();
                        }));
                        tableActions.appendChild(iconButton('fa-minus', t('BlockRemoveRow'), 'btn-default', function () {
                            if (rows.length > 1) { rows.pop(); markDirty(); redraw(); }
                        }));
                        tableActions.appendChild(iconButton('fa-minus', t('BlockRemoveColumn'), 'btn-default', function () {
                            if ((rows[0] || []).length > 1) { rows.forEach(function (row) { row.pop(); }); markDirty(); redraw(); }
                        }));
                        body.appendChild(tableActions);
                        break;
                    case 'image':
                        body.appendChild(field(t('BlockFieldAttachment'), select(attachmentOptions(), block.attachmentId || '', function (v) { block.attachmentId = v ? Number(v) : undefined; }), id + '-attachment'));
                        body.appendChild(field(t('BlockFieldUrl'), input(block.url, function (v) { block.url = v; }, 'url', 1000), id + '-url'));
                        body.appendChild(field(t('BlockFieldAlt'), input(block.alt, function (v) { block.alt = v; }, 'text', 300), id + '-alt'));
                        body.appendChild(field(t('BlockFieldCaption'), input(block.title, function (v) { block.title = v; }, 'text', 300), id + '-caption'));
                        break;
                    case 'video':
                        body.appendChild(field(t('BlockFieldUrl'), input(block.url, function (v) { block.url = v; }, 'url', 1000), id + '-url'));
                        body.appendChild(field(t('BlockFieldLabel'), input(block.label, function (v) { block.label = v; }, 'text', 200), id + '-label'));
                        break;
                    case 'attachment':
                        body.appendChild(field(t('BlockFieldAttachment'), select(attachmentOptions(), block.attachmentId || '', function (v) { block.attachmentId = v ? Number(v) : undefined; }), id + '-attachment'));
                        body.appendChild(field(t('BlockFieldLabel'), input(block.label, function (v) { block.label = v; }, 'text', 200), id + '-label'));
                        break;
                    case 'link':
                        body.appendChild(field(t('BlockFieldLinkKind'), select([
                            { value: 'protocol', text: t('BlockLabelLinkProtocol') }, { value: 'poi', text: t('BlockLabelLinkPoi') },
                            { value: 'inventory', text: t('BlockLabelLinkInventory') }, { value: 'checklist', text: t('BlockLabelLinkChecklist') },
                            { value: 'training', text: t('BlockLabelLinkTraining') }, { value: 'url', text: t('BlockLabelLinkUrl') }
                        ], block.linkKind || 'protocol', function (v) { block.linkKind = v; redraw(); }), id + '-kind'));
                        if (block.linkKind === 'url') {
                            body.appendChild(field(t('BlockFieldUrl'), input(block.url, function (v) { block.url = v; }, 'url', 1000), id + '-url'));
                        } else if (block.linkKind === 'protocol' || block.linkKind === 'training') {
                            var choices = (block.linkKind === 'protocol' ? state.data.protocols : state.data.trainings) || [];
                            body.appendChild(field(t('BlockFieldLinkTarget'), select([{ value: '', text: t('BlockSelectNone') }].concat(choices.map(function (c) {
                                return { value: String(c.id), text: c.name };
                            })), block.refId || '', function (v) {
                                block.refId = v || undefined;
                                var chosen = choices.filter(function (c) { return String(c.id) === v; })[0];
                                if (chosen && !block.label) { block.label = chosen.name; redraw(); }
                            }), id + '-target'));
                        } else {
                            body.appendChild(field(t('BlockFieldLinkId'), input(block.refId, function (v) { block.refId = v; }, 'text', 64), id + '-ref'));
                        }
                        body.appendChild(field(t('BlockFieldLabel'), input(block.label, function (v) { block.label = v; }, 'text', 200), id + '-label'));
                        break;
                    case 'medication':
                        med = block.medication = block.medication || {};
                        var note = el('p', 'text-muted small');
                        note.appendChild(el('i', 'fa fa-info-circle')).setAttribute('aria-hidden', 'true');
                        note.appendChild(document.createTextNode(' ' + t('BlockDoseNoticeEditor')));
                        body.appendChild(note);
                        var medGrid = el('div', 'rgw-grid');
                        [['drug', 'BlockFieldDrug'], ['indication', 'BlockLabelIndication'], ['concentration', 'BlockLabelConcentration'],
                            ['dosePerKg', 'BlockLabelDosePerKg'], ['maxSingleDose', 'BlockLabelMaxSingleDose'], ['maxTotalDose', 'BlockLabelMaxTotalDose'],
                            ['route', 'BlockLabelRoute'], ['repeatInterval', 'BlockLabelRepeatInterval'], ['inventoryItemId', 'BlockFieldInventoryItemId'],
                            ['inventoryItemName', 'BlockFieldInventoryItemName']].forEach(function (pair) {
                            medGrid.appendChild(field(t(pair[1]), input(med[pair[0]], function (v) { med[pair[0]] = v; }, 'text', 200), id + '-' + pair[0]));
                        });
                        body.appendChild(medGrid);
                        body.appendChild(rich(med.notes, function (v) { med.notes = v; }, t('BlockFieldNotes'), id + '-notes'));
                        break;
                    case 'variants':
                        (block.variants || []).forEach(function (variant, vIndex) {
                            var panel = el('div', 'panel panel-default rgw-builder-section rgp-editor-variant');
                            var heading = el('div', 'panel-heading rgw-builder-heading');
                            heading.appendChild(el('span', 'rgw-builder-title', variant.label || t('BlockFieldVariantLabel')));
                            var vActions = el('span', 'rgw-builder-actions');
                            vActions.appendChild(iconButton('fa-times', t('BlockRemoveVariant'), 'btn-white', function () { block.variants.splice(vIndex, 1); markDirty(); redraw(); }));
                            heading.appendChild(vActions);
                            panel.appendChild(heading);
                            var vBody = el('div', 'panel-body');
                            vBody.appendChild(field(t('BlockFieldVariantLabel'), input(variant.label, function (v) { variant.label = v; }, 'text', 100), id + '-variant-' + variant.key));
                            variant.blocks = variant.blocks || [];
                            variant.blocks.forEach(function (child, cIndex) {
                                vBody.appendChild(renderBlock(child, variant.blocks, cIndex, true));
                            });
                            vBody.appendChild(addBar(variant.blocks, true));
                            panel.appendChild(vBody);
                            body.appendChild(panel);
                        });
                        body.appendChild(iconButton('fa-plus', t('BlockAddVariant'), 'btn-default', function () {
                            block.variants = block.variants || [];
                            block.variants.push({ key: newKey(), label: '', blocks: [] });
                            markDirty();
                            redraw();
                        }));
                        break;
                }
            }

            function attachmentOptions() {
                return [{ value: '', text: t('BlockSelectNone') }].concat((state.data.attachments || []).map(function (a) {
                    return { value: String(a.id), text: a.name };
                }));
            }

            function renderBlock(block, list, index, nested) {
                var id = 'blk-' + block.key;
                var panel = el('div', 'panel panel-default rgw-builder-item rgp-editor-block');
                panel.setAttribute('data-key', block.key);
                var heading = el('div', 'panel-heading rgw-builder-heading');
                heading.appendChild(el('span', 'rgw-builder-title', typeName(block.type)));
                var actions = el('span', 'rgw-builder-actions');
                var up = iconButton('fa-arrow-up', t('BlockMoveUp'), 'btn-white', function () {
                    if (moveIn(list, index, -1)) { redraw(block.key, 'up'); }
                });
                up.setAttribute('data-move', 'up');
                var down = iconButton('fa-arrow-down', t('BlockMoveDown'), 'btn-white', function () {
                    if (moveIn(list, index, 1)) { redraw(block.key, 'down'); }
                });
                down.setAttribute('data-move', 'down');
                actions.appendChild(up);
                actions.appendChild(down);
                actions.appendChild(iconButton('fa-trash', t('BlockRemove'), 'btn-white', function () {
                    list.splice(index, 1);
                    markDirty();
                    redraw();
                }));
                heading.appendChild(actions);
                panel.appendChild(heading);
                var body = el('div', 'panel-body');
                renderFields(block, body, id);
                var scopeBox = scopes(block, id);
                if (scopeBox) {
                    body.appendChild(scopeBox);
                }
                panel.appendChild(body);
                return panel;
            }

            function addBar(list, nested) {
                var bar = el('div', 'rgw-toolbar rgp-editor-add');
                bar.setAttribute('role', 'toolbar');
                bar.setAttribute('aria-label', t('BlockAddBlock'));
                bar.appendChild(el('span', 'control-label', t('BlockAddBlock')));
                var types = nested
                    ? ['paragraph', 'steps', 'caution', 'warning', 'gate', 'table', 'image', 'video', 'attachment', 'link', 'medication']
                    : ['heading', 'paragraph', 'steps', 'caution', 'warning', 'gate', 'table', 'image', 'video', 'attachment', 'link', 'medication', 'variants'];
                var icons = {
                    heading: 'fa-header', paragraph: 'fa-paragraph', steps: 'fa-list-ol', caution: 'fa-exclamation-triangle', warning: 'fa-exclamation-circle',
                    gate: 'fa-phone', table: 'fa-table', image: 'fa-picture-o', video: 'fa-video-camera', attachment: 'fa-paperclip', link: 'fa-link',
                    medication: 'fa-medkit', variants: 'fa-columns'
                };
                types.forEach(function (type) {
                    bar.appendChild(iconButton(icons[type], typeName(type), 'btn-default', function () {
                        var block = blankBlock(type);
                        list.push(block);
                        markDirty();
                        redraw(block.key, 'focus');
                    }));
                });
                return bar;
            }

            function redraw(focusKey, how) {
                var host = document.getElementById('blockList');
                if (!host) {
                    return;
                }
                host.innerHTML = '';
                if (!state.doc.blocks.length) {
                    host.appendChild(el('p', 'text-muted', t('BlockEditorEmpty')));
                }
                state.doc.blocks.forEach(function (block, index) {
                    host.appendChild(renderBlock(block, state.doc.blocks, index, false));
                });
                host.appendChild(addBar(state.doc.blocks, false));
                if (focusKey) {
                    var panel = host.querySelector('[data-key="' + focusKey + '"]');
                    if (panel) {
                        var target = how === 'up' || how === 'down'
                            ? panel.querySelector('[data-move="' + how + '"]')
                            : panel.querySelector('input, select, [contenteditable="true"]');
                        if (target) {
                            target.focus();
                        }
                    }
                }
                syncHidden();
            }

            function serialize() {
                return JSON.stringify({ schema: 2, blocks: state.doc.blocks });
            }

            function syncHidden() {
                var hidden = document.getElementById('ContentJson');
                if (hidden) {
                    hidden.value = serialize();
                }
            }

            function token($form) {
                return $form.find('input[name="__RequestVerificationToken"]').val();
            }

            function preview() {
                var $editor = $('#blockEditor');
                var $panel = $('#blockPreview');
                $panel.attr('aria-busy', 'true');
                $.ajax({
                    url: $editor.data('preview-url'),
                    type: 'POST',
                    data: { contentJson: serialize(), __RequestVerificationToken: token($('#protocolEditorForm')) }
                }).done(function (result) {
                    // The server returns the version as members read it, already sanitized for display.
                    $panel.html(result && result.html ? result.html : '');
                }).fail(function () {
                    $panel.text(t('BlockPreviewFailed'));
                }).always(function () {
                    $panel.attr('aria-busy', 'false');
                });
            }

            $(document).ready(function () {
                var dataNode = document.getElementById('protocolBlocksData');
                if (!dataNode) {
                    return;
                }
                try {
                    var parsed = JSON.parse(dataNode.textContent || '{}');
                    state.data = parsed;
                    state.text = parsed.text || {};
                    state.doc = parsed.document && parsed.document.blocks ? parsed.document : { schema: 2, blocks: [] };
                } catch (e) {
                    state.doc = { schema: 2, blocks: [] };
                }
                state.doc.schema = 2;
                redraw();

                var $form = $('#protocolEditorForm');
                $form.on('submit', function () {
                    syncHidden();
                    return true;
                });
                $('#blockPreviewButton').prop('disabled', false).on('click', preview);

                var autosaveUrl = $form.data('autosave-url');
                var saving = false;
                var $status = $('#autosaveStatus');
                $('#Library_ChangeSummary').on('input', markDirty);
                // P4: the case flow builder's graph is draft content too.
                $(document).on('resgrid:graph-changed', markDirty);
                if (autosaveUrl && $form.data('content-locked') !== true && $form.data('content-locked') !== 'true') {
                    window.setInterval(function () {
                        if (!state.dirty || saving || !autosaveUrl) {
                            return;
                        }
                        saving = true;
                        state.dirty = false;
                        syncHidden();
                        $status.text(t('AutosaveSaving'));
                        $.ajax({
                            url: autosaveUrl,
                            type: 'POST',
                            data: {
                                contentJson: serialize(),
                                decisionGraphJson: $('#DecisionGraphJson').length ? $('#DecisionGraphJson').val() : undefined,
                                changeSummary: $('#Library_ChangeSummary').val(),
                                requiresAcknowledgment: $('#Library_RequiresAcknowledgment').is(':checked'),
                                draftRowVersion: $('#DraftRowVersion').val(),
                                __RequestVerificationToken: token($form)
                            }
                        }).done(function (result) {
                            if (result && result.success) {
                                $('#DraftRowVersion').val(result.rowVersion);
                                $status.removeClass('text-danger').addClass('text-muted').text(t('AutosaveSaved') + ' ' + new Date().toLocaleTimeString());
                            } else {
                                $status.removeClass('text-muted').addClass('text-danger').text((result && result.message) || t('AutosaveFailed'));
                                if (result && result.error === 'StaleCopy') {
                                    autosaveUrl = null;
                                }
                            }
                        }).fail(function () {
                            state.dirty = true;
                            $status.removeClass('text-muted').addClass('text-danger').text(t('AutosaveFailed'));
                        }).always(function () {
                            saving = false;
                        });
                    }, 30000);
                }
            });
        })(blocks = protocols.blocks || (protocols.blocks = {}));
    })(protocols = resgrid.protocols || (resgrid.protocols = {}));
})(resgrid || (resgrid = {}));
