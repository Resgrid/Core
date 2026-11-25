
var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var protocolpanel;
        (function (protocolpanel) {
            // The protocol panel of the call forms (New Call, Update Call, Add Archived Call). The server matches the call's type,
            // priority and logged time against the department's protocols (Protocols/GetProtocolsForCall, the same matcher the
            // v4 API uses) and returns each protocol with the trigger that matched. The panel shows the suggestions with that
            // reason, runs each protocol's questions as an inline stepper with no answer chosen in advance, and lets the
            // dispatcher add a protocol by hand. It posts only which protocols were kept (selectedProtocol_{id}) and the chosen
            // answer ids (protocolAnswer_{protocolId}_{questionId}); the server recomputes the score and decides.
            var options = {};
            var request = 0;
            var all = [];
            var manual = {};
            var answers = {};

            function text(key, fallback) {
                var strings = window.resgridProtocolPanelStrings || {};
                return strings[key] || fallback || key;
            }

            function format(template) {
                var args = Array.prototype.slice.call(arguments, 1);
                return String(template).replace(/\{(\d+)\}/g, function (m, i) { return args[i] !== undefined ? args[i] : m; });
            }

            function esc(value) {
                return resgrid.escapeHtml(value === null || value === undefined ? '' : String(value));
            }

            function panel() { return $('#protocolPanel'); }

            function find(id) {
                for (var i = 0; i < all.length; i++) {
                    if (all[i].Id === id) return all[i];
                }
                return null;
            }

            function score(protocol) {
                var chosen = answers[protocol.Id] || {};
                var total = 0, answered = 0;
                protocol.Questions.forEach(function (q) {
                    var answerId = chosen[q.Id];
                    if (answerId === undefined) return;
                    answered++;
                    q.Answers.forEach(function (a) { if (a.Id === answerId) total += Number(a.Weight) || 0; });
                });
                return { total: total, complete: answered === protocol.Questions.length };
            }

            function outcome(protocol, isManual) {
                if (!protocol.Questions.length) return { attachable: true, note: '' };
                var s = score(protocol);
                if (!s.complete) {
                    return { attachable: isManual, note: text('answerToContinue', 'Answer every question to decide whether this protocol applies.') };
                }
                var passed = s.total >= protocol.MinimumWeight;
                var summary = format(text('scoreOfMinimum', 'Score {0} of minimum {1}'), s.total, protocol.MinimumWeight);
                if (passed) return { attachable: true, passed: true, note: summary + ' · ' + text('applies', 'Applies') };
                return { attachable: isManual, passed: false, note: summary + ' · ' + text('doesNotApply', 'Does not apply') };
            }

            function stepperHtml(protocol) {
                if (!protocol.Questions.length) return '';
                var chosen = answers[protocol.Id] || {};
                var html = '<div class="protocol-stepper" data-step="0">';
                protocol.Questions.forEach(function (q, index) {
                    var group = 'protocolAnswer_' + protocol.Id + '_' + q.Id;
                    html += '<fieldset class="protocol-step" data-index="' + index + '"' + (index === 0 ? '' : ' hidden') + '>' +
                        '<legend class="h5">' + esc(format(text('questionOf', 'Question {0} of {1}'), index + 1, protocol.Questions.length)) + ' — ' + esc(q.Question) + '</legend>';
                    q.Answers.forEach(function (a) {
                        var id = group + '_' + a.Id;
                        html += '<div class="radio"><label for="' + id + '"><input type="radio" id="' + id + '" name="' + group + '" value="' + a.Id + '"' +
                            (chosen[q.Id] === a.Id ? ' checked' : '') + ' data-protocol="' + protocol.Id + '" data-question="' + q.Id + '"> ' + esc(a.Answer) + '</label></div>';
                    });
                    html += '<div class="btn-group">' +
                        (index > 0 ? '<button type="button" class="btn btn-default btn-xs protocol-step-back"><i class="fa fa-chevron-left" aria-hidden="true"></i> ' + esc(text('back', 'Back')) + '</button>' : '') +
                        (index < protocol.Questions.length - 1 ? '<button type="button" class="btn btn-default btn-xs protocol-step-next"><i class="fa fa-chevron-right" aria-hidden="true"></i> ' + esc(text('next', 'Next')) + '</button>' : '') +
                        '</div></fieldset>';
                });
                html += '<p class="protocol-outcome text-muted small" role="status"></p></div>';
                return html;
            }

            function rowHtml(protocol, isManual) {
                var reason = isManual
                    ? '<span class="label label-info"><i class="fa fa-hand-pointer-o" aria-hidden="true"></i> ' + esc(text('addedByHand', 'Added by hand')) + '</span>'
                    : '<span class="label label-primary"><i class="fa fa-bolt" aria-hidden="true"></i> ' + esc(protocol.Reason) + '</span>';
                var checkboxId = 'selectedProtocol_' + protocol.Id;
                return '<div class="panel panel-default rgw-builder-item protocol-row" data-protocol-id="' + protocol.Id + '" data-manual="' + (isManual ? '1' : '0') + '">' +
                    '<div class="panel-heading rgw-builder-heading">' +
                    '<label class="rgw-builder-title" for="' + checkboxId + '"><input type="checkbox" class="protocol-select" id="' + checkboxId + '" name="' + checkboxId + '" value="1"> ' +
                    '<span class="rgw-mono">' + esc(protocol.Code) + '</span> ' + esc(protocol.Name) + '</label>' +
                    '<span class="rgw-builder-actions">' + reason +
                    (isManual ? ' <button type="button" class="btn btn-xs btn-default protocol-remove-manual"><i class="fa fa-times" aria-hidden="true"></i> ' + esc(text('remove', 'Remove')) + '<span class="sr-only"> ' + esc(protocol.Name) + '</span></button>' : '') +
                    '</span></div>' +
                    (protocol.Questions.length ? '<div class="panel-body">' + stepperHtml(protocol) + '</div>' : '') +
                    '</div>';
            }

            function applyOutcome($row) {
                var protocol = find(Number($row.data('protocol-id')));
                if (!protocol) return;
                var isManual = $row.data('manual') === 1 || $row.data('manual') === '1';
                var result = outcome(protocol, isManual);
                var $box = $row.find('.protocol-select');
                $box.prop('disabled', !result.attachable);
                if (!result.attachable) {
                    $box.prop('checked', false);
                } else if (!$row.data('touched')) {
                    // Suggested without questions, picked by hand, or questions answered and passed: on by default.
                    $box.prop('checked', !protocol.Questions.length || isManual || result.passed === true);
                }
                $row.find('.protocol-outcome').text(result.note);
            }

            function render() {
                var $rows = panel().find('.protocol-panel-rows').empty();
                var suggested = all.filter(function (p) { return p.State === 1 || p.State === 2; });

                suggested.forEach(function (p) { $rows.append(rowHtml(p, false)); });
                Object.keys(manual).forEach(function (id) {
                    var p = find(Number(id));
                    if (p && suggested.indexOf(p) < 0) $rows.append(rowHtml(p, true));
                    else delete manual[id];
                });

                $rows.find('.protocol-row').each(function () { applyOutcome($(this)); });
                panel().find('.protocol-panel-empty').prop('hidden', $rows.find('.protocol-row').length > 0);

                var $pick = panel().find('#protocolManualPick').empty();
                $pick.append('<option value="">' + esc(text('choose', 'Choose a protocol…')) + '</option>');
                all.forEach(function (p) {
                    if (suggested.indexOf(p) < 0 && !manual[p.Id]) {
                        $pick.append('<option value="' + p.Id + '">' + esc(p.Code + ' — ' + p.Name) + '</option>');
                    }
                });
                panel().find('.protocol-panel-manual').prop('hidden', $pick.find('option').length <= 1);
            }

            function refresh() {
                if (!panel().length) return;
                var current = ++request;
                var url = resgrid.absoluteBaseUrl + '/User/Protocols/GetProtocolsForCall?priority=' + encodeURIComponent(options.getPriority ? options.getPriority() || '' : '') +
                    '&type=' + encodeURIComponent(options.getType ? options.getType() || '' : '');
                var loggedOn = options.getLoggedOn ? options.getLoggedOn() : null;
                if (loggedOn) url += '&loggedOn=' + encodeURIComponent(loggedOn);
                var callId = options.getCallId ? options.getCallId() : null;
                if (callId) url += '&callId=' + encodeURIComponent(callId);

                $.ajax({ url: url, contentType: 'application/json', type: 'GET' }).done(function (data) {
                    // Type, priority and template changes can overlap; only the latest request's protocols are shown.
                    if (current !== request) return;
                    all = data || [];
                    render();
                });
            }
            protocolpanel.refresh = refresh;

            function init(settings) {
                options = settings || {};
                var $panel = panel();
                if (!$panel.length) return;

                $panel.on('change', 'input[type=radio]', function () {
                    var protocolId = Number($(this).data('protocol'));
                    var questionId = Number($(this).data('question'));
                    answers[protocolId] = answers[protocolId] || {};
                    answers[protocolId][questionId] = Number($(this).val());
                    applyOutcome($(this).closest('.protocol-row'));
                });

                $panel.on('change', '.protocol-select', function () {
                    $(this).closest('.protocol-row').data('touched', true);
                });

                $panel.on('click', '.protocol-step-next, .protocol-step-back', function () {
                    var $step = $(this).closest('.protocol-step');
                    var $target = $(this).hasClass('protocol-step-next') ? $step.next('.protocol-step') : $step.prev('.protocol-step');
                    if (!$target.length) return;
                    $step.prop('hidden', true);
                    $target.prop('hidden', false);
                    $target.find('input[type=radio]').first().trigger('focus');
                });

                $panel.on('click', '#protocolManualAdd', function () {
                    var id = Number($('#protocolManualPick').val());
                    var protocol = find(id);
                    if (!protocol) return;

                    // A protocol that did not match arrives without its questions: read them now it is picked.
                    if (protocol.HasQuestions && !protocol.Questions.length) {
                        $.ajax({ url: resgrid.absoluteBaseUrl + '/User/Protocols/GetProtocol?id=' + encodeURIComponent(id), type: 'GET' }).done(function (detail) {
                            protocol.Questions = (detail && detail.Questions) || [];
                            manual[id] = true;
                            render();
                        });
                        return;
                    }

                    manual[id] = true;
                    render();
                });

                $panel.on('click', '.protocol-remove-manual', function () {
                    delete manual[$(this).closest('.protocol-row').data('protocol-id')];
                    render();
                });

                refresh();
            }
            protocolpanel.init = init;
        })(protocolpanel = dispatch.protocolpanel || (dispatch.protocolpanel = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
