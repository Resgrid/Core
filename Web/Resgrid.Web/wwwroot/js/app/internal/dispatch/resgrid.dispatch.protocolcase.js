
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var protocolcase;
        (function (protocolcase) {
            // Protocols P4: the case flow inside the call forms' protocol panel. For a protocol whose version in force has a case flow,
            // the dispatcher walks entry questions, key questions and instructions to an outcome: its determinant, the suggested priority
            // (applied to the form once, and changeable), the response hint and the post-dispatch instructions. A different determinant
            // needs a reason. The panel posts protocolCase_{id} (answers, override, applied priority, a confirmed AI suggestion); the
            // server walks the cited version's graph again and records the case: the client decides nothing but the answers.
            var definitions = {};
            var cases = {};
            var options = {};
            var aiAvailable = false;
            var loaded = false;

            function text(key, fallback) {
                var strings = window.resgridProtocolPanelStrings || {};
                return strings[key] || fallback || key;
            }

            function esc(value) { return resgrid.escapeHtml(value === null || value === undefined ? '' : String(value)); }

            function state(protocolId) {
                if (!cases[protocolId]) cases[protocolId] = { answers: {}, overrideDeterminant: null, overrideReason: null, appliedPriority: null, aiSuggestionId: null, appliedFor: null };
                return cases[protocolId];
            }

            function find(graph, key) {
                for (var i = 0; i < graph.nodes.length; i++) { if (graph.nodes[i] && graph.nodes[i].key === key) return graph.nodes[i]; }
                return null;
            }

            // ProtocolCaseFlow.Run, in short: stop at the first unanswered question, pass instructions, end at an outcome.
            function run(graph, answers) {
                var result = { status: 'invalid', current: null, steps: [], outcome: null };
                var roots = graph.nodes.filter(function (n) { return n && n.root; });
                var node = roots.length === 1 ? roots[0] : null;
                var seen = {};
                while (node) {
                    if (seen[node.key]) return result;
                    seen[node.key] = true;
                    if (node.kind === 3) { result.outcome = node; result.status = 'complete'; return result; }
                    if (node.kind === 2) { result.steps.push({ node: node }); node = find(graph, node.next); continue; }
                    var raw = answers[node.key];
                    if (raw === undefined || raw === null || String(raw).trim() === '') { result.status = 'progress'; result.current = node; return result; }
                    var next = null, label = raw;
                    if (node.questionType === 4) { next = node.next; }
                    else if (node.questionType === 3) {
                        var value = Number(raw);
                        var range = (node.answers || []).filter(function (a) { return (a.min === null || a.min === undefined || value >= a.min) && (a.max === null || a.max === undefined || value <= a.max); })[0];
                        if (!range || isNaN(value)) { result.current = node; result.status = 'progress'; return result; }
                        next = range.next;
                    } else {
                        var answer = (node.answers || []).filter(function (a) { return a.key === raw; })[0];
                        if (!answer) { result.current = node; result.status = 'progress'; return result; }
                        next = answer.next;
                        label = answer.label || (answer.key === 'yes' ? text('yes', 'Yes') : answer.key === 'no' ? text('no', 'No') : answer.key);
                    }
                    result.steps.push({ node: node, answer: label });
                    node = find(graph, next);
                }
                return result;
            }

            function questionHtml(protocolId, node) {
                var name = 'pcase_' + protocolId + '_' + node.key;
                var stage = node.stage === 0 ? text('entryQuestion', 'Entry question') : text('keyQuestion', 'Key question');
                var html = '<fieldset class="protocol-case-question"><legend class="h5"><span class="label label-default">' + esc(stage) + '</span> ' + esc(node.text || node.label) + '</legend>';
                if (node.questionType === 1 || node.questionType === 2) {
                    (node.answers || []).forEach(function (a) {
                        var label = a.label || (a.key === 'yes' ? text('yes', 'Yes') : text('no', 'No'));
                        html += '<button type="button" class="btn btn-default btn-sm protocol-case-answer" data-protocol="' + protocolId + '" data-node="' + esc(node.key) + '" data-value="' + esc(a.key) + '">' + esc(label) + '</button> ';
                    });
                } else {
                    var type = node.questionType === 3 ? 'number' : 'text';
                    html += '<div class="form-inline"><label class="sr-only" for="' + name + '">' + esc(node.text || node.label) + '</label>' +
                        '<input class="form-control input-sm protocol-case-value" type="' + type + '" step="any" id="' + name + '" maxlength="500" /> ' +
                        '<button type="button" class="btn btn-primary btn-sm protocol-case-submit" data-protocol="' + protocolId + '" data-node="' + esc(node.key) + '"><i class="fa fa-chevron-right" aria-hidden="true"></i> ' + esc(text('next', 'Next')) + '</button></div>';
                }
                return html + '</fieldset>';
            }

            function html(protocolId) {
                var def = definitions[protocolId];
                if (!def) return '';
                var s = state(protocolId);
                var walked = run(def.graph, s.answers);
                var out = '<div class="protocol-case" data-protocol="' + protocolId + '">' +
                    '<p class="h5"><i class="fa fa-sitemap" aria-hidden="true"></i> ' + esc(text('caseFlow', 'Case flow')) + '</p>';
                if (s.aiSuggestionId) {
                    out += '<p class="alert alert-info small" role="status"><i class="fa fa-magic" aria-hidden="true"></i> ' + esc(text('aiPrefilled', 'Suggested by Enhanced AI. Check every answer before you save.')) + '</p>';
                }
                if (walked.steps.length) {
                    out += '<ol class="protocol-case-path small">';
                    walked.steps.forEach(function (step) {
                        if (step.node.kind === 2) {
                            out += '<li class="protocol-case-instruction"><strong>' + esc(text('instruction', 'Instruction')) + ':</strong> ' + esc(step.node.text) +
                                (step.node.speak ? ' <button type="button" class="btn btn-xs btn-default protocol-case-play" data-protocol="' + protocolId + '" data-node="' + esc(step.node.key) + '"><i class="fa fa-volume-up" aria-hidden="true"></i> ' + esc(text('play', 'Play')) + '</button>' : '') + '</li>';
                        } else {
                            out += '<li>' + esc(step.node.text || step.node.label) + ' <strong>' + esc(step.answer) + '</strong> ' +
                                '<button type="button" class="btn btn-link btn-xs protocol-case-change" data-protocol="' + protocolId + '" data-node="' + esc(step.node.key) + '">' + esc(text('change', 'Change')) + '<span class="sr-only"> ' + esc(step.node.text || step.node.label) + '</span></button></li>';
                        }
                    });
                    out += '</ol>';
                }
                if (walked.status === 'progress' && walked.current) {
                    out += questionHtml(protocolId, walked.current);
                } else if (walked.status === 'complete') {
                    var o = walked.outcome;
                    var determinant = s.overrideDeterminant || o.determinant;
                    out += '<div class="protocol-case-outcome rgw-next-action" role="status"><i class="fa fa-flag-checkered" aria-hidden="true"></i> ' +
                        esc(text('determinant', 'Determinant')) + ' <span class="label label-danger rgw-mono">' + esc(determinant) + '</span>' +
                        (s.overrideDeterminant ? ' <span class="label label-warning">' + esc(text('overridden', 'Overridden')) + '</span>' : '') +
                        (o.priorityName ? ' · ' + esc(text('priority', 'Priority')) + ' <strong>' + esc(o.priorityName) + '</strong>' + (s.appliedFor === o.key ? ' <span class="text-muted">(' + esc(text('priorityApplied', 'applied')) + ')</span>' : '') : '') +
                        (o.response ? ' · ' + esc(o.response) : '') + '</div>';
                    if (o.postDispatch) {
                        out += '<p class="small"><strong>' + esc(text('postDispatch', 'After dispatch')) + ':</strong> ' + esc(o.postDispatch) + '</p>';
                    }
                    out += '<details class="protocol-case-override"' + (s.overrideDeterminant ? ' open' : '') + '><summary>' + esc(text('overrideDeterminant', 'Override the determinant')) + '</summary><div class="form-inline m-t-xs">' +
                        '<label class="sr-only" for="pcase_override_' + protocolId + '">' + esc(text('determinant', 'Determinant')) + '</label>' +
                        '<input class="form-control input-sm rgw-mono protocol-case-override-value" id="pcase_override_' + protocolId + '" maxlength="32" data-protocol="' + protocolId + '" value="' + esc(s.overrideDeterminant) + '" placeholder="' + esc(o.determinant) + '" /> ' +
                        '<label class="sr-only" for="pcase_reason_' + protocolId + '">' + esc(text('overrideReason', 'Reason')) + '</label>' +
                        '<input class="form-control input-sm protocol-case-override-reason" id="pcase_reason_' + protocolId + '" maxlength="500" data-protocol="' + protocolId + '" value="' + esc(s.overrideReason) + '" placeholder="' + esc(text('overrideReason', 'Reason (required)')) + '" style="min-width: 240px;" />' +
                        '</div></details>';
                } else {
                    out += '<p class="text-warning small">' + esc(text('caseInvalid', 'This case flow could not be walked.')) + '</p>';
                }
                return out + '</div>';
            }

            function payload(protocolId) {
                var s = state(protocolId);
                return JSON.stringify({
                    answers: s.answers,
                    overrideDeterminant: s.overrideDeterminant || null,
                    overrideReason: s.overrideReason || null,
                    appliedPriority: s.appliedPriority,
                    aiSuggestionId: s.aiSuggestionId
                });
            }

            function refreshRow(protocolId) {
                var $row = $('#protocolPanel .protocol-row[data-protocol-id="' + protocolId + '"]');
                if (!$row.length || !definitions[protocolId]) return;
                var $box = $row.find('.protocol-case-host');
                if (!$box.length) {
                    $box = $('<div class="panel-body protocol-case-host"></div>');
                    $row.append($box);
                }
                $box.html(html(protocolId) + '<input type="hidden" name="protocolCase_' + protocolId + '" />');
                $box.find('input[name="protocolCase_' + protocolId + '"]').val(payload(protocolId));
                applyPriority(protocolId);
            }

            // The outcome's priority is applied to the form once per outcome reached; the dispatcher can still change it.
            function applyPriority(protocolId) {
                var def = definitions[protocolId];
                var s = state(protocolId);
                var walked = run(def.graph, s.answers);
                var outcome = walked.status === 'complete' ? walked.outcome : null;
                if (!outcome || outcome.priority === null || outcome.priority === undefined || s.appliedFor === outcome.key || !options.setPriority) return;
                s.appliedFor = outcome.key;
                s.appliedPriority = outcome.priority;
                options.setPriority(String(outcome.priority));
                refreshRow(protocolId);
            }

            function answer(protocolId, nodeKey, value) {
                var s = state(protocolId);
                s.answers[nodeKey] = value;
                refreshRow(protocolId);
                $('#protocolPanel .protocol-case[data-protocol="' + protocolId + '"]').find('button,input').filter(':visible').first().trigger('focus');
            }

            protocolcase.decorate = function () {
                Object.keys(definitions).forEach(function (id) { refreshRow(Number(id)); });
            };

            protocolcase.has = function (protocolId) { return !!definitions[protocolId]; };

            protocolcase.init = function (settings) {
                options = settings || {};
                var callId = options.getCallId ? options.getCallId() : null;
                $.ajax({ url: resgrid.absoluteBaseUrl + '/User/ProtocolCases/Definitions' + (callId ? '?callId=' + encodeURIComponent(callId) : ''), type: 'GET' }).done(function (data) {
                    (data && data.protocols || []).forEach(function (p) {
                        if (p && p.graph && p.graph.nodes) definitions[p.protocolId] = p;
                    });
                    aiAvailable = !!(data && data.aiAvailable);
                    loaded = true;
                    $('#protocolCaseAi').prop('hidden', !aiAvailable);
                    protocolcase.decorate();
                });

                var $panel = $('#protocolPanel');
                $panel.on('click', '.protocol-case-answer', function () {
                    answer(Number($(this).data('protocol')), String($(this).data('node')), String($(this).data('value')));
                });
                $panel.on('click', '.protocol-case-submit', function () {
                    var value = $(this).closest('fieldset').find('.protocol-case-value').val();
                    if (value === null || String(value).trim() === '') return;
                    answer(Number($(this).data('protocol')), String($(this).data('node')), String(value).trim());
                });
                $panel.on('keydown', '.protocol-case-value', function (e) {
                    if (e.key === 'Enter') { e.preventDefault(); $(this).closest('fieldset').find('.protocol-case-submit').trigger('click'); }
                });
                $panel.on('click', '.protocol-case-change', function () {
                    // Going back to a question drops it and everything answered after it on the path.
                    var protocolId = Number($(this).data('protocol'));
                    var key = String($(this).data('node'));
                    var s = state(protocolId);
                    var walked = run(definitions[protocolId].graph, s.answers);
                    var drop = false;
                    walked.steps.forEach(function (step) { if (step.node.key === key) drop = true; if (drop) delete s.answers[step.node.key]; });
                    s.overrideDeterminant = null;
                    s.overrideReason = null;
                    refreshRow(protocolId);
                });
                $panel.on('input', '.protocol-case-override-value, .protocol-case-override-reason', function () {
                    var protocolId = Number($(this).data('protocol'));
                    var s = state(protocolId);
                    if ($(this).hasClass('protocol-case-override-value')) s.overrideDeterminant = $(this).val().trim() || null;
                    else s.overrideReason = $(this).val().trim() || null;
                    $(this).closest('.protocol-case-host').find('input[name="protocolCase_' + protocolId + '"]').val(payload(protocolId));
                });
                $panel.on('click', '.protocol-case-play', function () {
                    var $button = $(this).prop('disabled', true);
                    $.ajax({ url: resgrid.absoluteBaseUrl + '/User/ProtocolCases/InstructionAudio?protocolId=' + encodeURIComponent($(this).data('protocol')) + '&node=' + encodeURIComponent($(this).data('node')), type: 'GET' })
                        .done(function (result) {
                            var urls = (result && result.urls) || [];
                            (function play(i) {
                                if (i >= urls.length) { $button.prop('disabled', false); return; }
                                var audio = new Audio(urls[i]);
                                audio.onended = function () { play(i + 1); };
                                audio.onerror = function () { $button.prop('disabled', false); };
                                audio.play().catch(function () { $button.prop('disabled', false); });
                            })(0);
                        }).fail(function () { $button.prop('disabled', false); });
                });
                $panel.on('click', '#protocolCaseAiButton', function () {
                    var id = options.getCallId ? options.getCallId() : null;
                    if (!id) return;
                    var $status = $('#protocolCaseAiStatus').text(text('aiAsking', 'Asking Enhanced AI…'));
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/ProtocolCases/Suggest',
                        type: 'POST',
                        data: { callId: id, __RequestVerificationToken: $('input[name="__RequestVerificationToken"]').first().val() }
                    }).done(function (result) {
                        if (!result || !result.success) { $status.text((result && result.message) || ''); return; }
                        var s = state(result.protocolId);
                        s.answers = result.answers || {};
                        s.aiSuggestionId = result.suggestionId;
                        $status.text(result.code + ' ' + result.name + (result.rationale ? ' — ' + result.rationale : ''));
                        if (options.addProtocol) options.addProtocol(result.protocolId);
                        refreshRow(result.protocolId);
                    }).fail(function () { $status.text(text('aiFailed', 'The suggestion could not be made.')); });
                });
            };
        })(protocolcase = dispatch.protocolcase || (dispatch.protocolcase = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
