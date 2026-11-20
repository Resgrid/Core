var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var destinationadvice;
        (function (destinationadvice) {
            // Enhanced AI destination advice panel (_DestinationAdvice.cshtml; call-intake-and-ai-destinations-plan.md D4). Advice never
            // changes the destination by itself: Apply sets it through the call save, Dismiss hides it. Reloads on the
            // aiDestinationAdviceUpdated eventing event for this call.
            // loadGeneration: only the newest read may render, and an action invalidates reads already in flight (a GET that started
            // before Dismiss must not bring the dismissed advice back). refreshPending: a refresh that arrived during an action runs after it.
            var panel, callId, canApply = false, current = null, busy = false, refreshTimer = null, refreshPending = false, loadGeneration = 0, loadsInFlight = 0;

            function strings() { return window.destinationAdviceStrings || {}; }
            function text(key, fallback) { var s = strings(); return s[key] || fallback || ''; }
            function format(template, value) { return String(template).replace('{0}', value); }
            function esc(value) {
                if (resgrid.escapeHtml) return resgrid.escapeHtml(value == null ? '' : String(value));
                return $('<div>').text(value == null ? '' : String(value)).html();
            }
            function token() {
                return panel.find('input[name="__RequestVerificationToken"]').val() || $('input[name="__RequestVerificationToken"]').first().val();
            }
            function outcomeText(outcome) {
                var outcomes = strings().outcomes || {};
                return outcomes[outcome] || '';
            }
            function message(html, css) {
                var box = $('#destinationAdviceMessage');
                if (!html) { box.hide().empty(); return; }
                box.attr('class', 'small ' + (css || 'text-muted')).html(html).show();
            }

            function load() {
                var generation = ++loadGeneration;
                loadsInFlight++;
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/AiDestinations/Advice',
                    data: { callId: callId },
                    type: 'GET',
                    cache: false
                }).done(function (response) {
                    if (generation !== loadGeneration) return;
                    render(response);
                }).fail(function () {
                    // A failed refresh keeps what is shown.
                }).always(function () {
                    loadsInFlight--;
                });
            }
            destinationadvice.load = load;

            function scheduleLoad() {
                if (refreshTimer) clearTimeout(refreshTimer);
                refreshTimer = setTimeout(function () {
                    refreshTimer = null;
                    if (busy) { refreshPending = true; return; }
                    load();
                }, 500);
            }

            function render(response) {
                if (!response || response.available === false || (response.success === false && !response.advice && !response.outcome)) {
                    panel.hide();
                    return;
                }

                panel.show();
                canApply = !!response.canApply;
                $('#destinationAdviceAsk').toggle(canApply);

                var advice = response.advice;
                current = advice || null;
                var body = $('#destinationAdviceBody');
                if (!advice) {
                    body.html('<span class="text-muted">' + esc(text('noAdvice')) + '</span>');
                    $('#destinationAdviceActions').hide();
                    return;
                }

                var html = '';
                if (advice.recommendedPoiId) {
                    html += '<div><strong>' + esc(format(text('consider', 'Consider {0}'), advice.recommendedPoiName || ('#' + advice.recommendedPoiId))) + '</strong>';
                    if (advice.recommendedPoiStatus === 1 || advice.recommendedPoiStatus === 2 || advice.recommendedPoiStatus === 3) {
                        html += ' <span class="label label-' + (advice.recommendedPoiStatus === 1 ? 'info' : 'warning') + '">' +
                            esc(text('status' + advice.recommendedPoiStatus)) + '</span>';
                    }
                    html += '</div>';
                } else {
                    html += '<div><strong>' + esc(advice.currentPoiId ? text('keep') : text('noSuggestion')) + '</strong></div>';
                }

                var currentText = advice.currentPoiName ? format(text('current'), advice.currentPoiName)
                    : panel.data('free-destination') === true || panel.data('free-destination') === 'true' ? text('currentFree') : text('currentNone');
                html += '<div class="small">' + esc(currentText) + '</div>';

                if (advice.requiredCapabilities && advice.requiredCapabilities.length) {
                    html += '<div class="small">' + esc(format(text('needs'), advice.requiredCapabilities.map(function (c) { return c.name; }).join(', '))) + '</div>';
                }
                if (advice.rationale) {
                    html += '<div style="margin-top: 4px;">' + esc(advice.rationale) + '</div>';
                }

                var when = advice.createdOn ? new Date(advice.createdOn) : null;
                html += '<div class="small text-muted" style="margin-top: 4px;">' + esc(format(text('confidence'), Math.round((advice.confidence || 0) * 100))) +
                    (when && !isNaN(when.getTime()) ? ' &middot; ' + esc(format(text('advisedAt'), when.toLocaleString())) : '') + '</div>';
                if (advice.isChange && !advice.isConfident) {
                    html += '<div class="small text-warning">' + esc(text('lowConfidence')) + '</div>';
                }
                body.html(html);

                var open = advice.state === 1;
                $('#destinationAdviceActions').toggle(canApply && open);
                $('#destinationAdviceApply').toggle(!!advice.recommendedPoiId);
            }

            function post(action, data, done) {
                if (busy) return;
                busy = true;
                // A read in flight is now stale; whatever change it was fetching is read again once this action is done.
                if (loadsInFlight > 0) refreshPending = true;
                loadGeneration++;
                data.__RequestVerificationToken = token();
                $('#destinationAdviceAsk, #destinationAdviceApply, #destinationAdviceDismiss').prop('disabled', true);
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/AiDestinations/' + action,
                    type: 'POST',
                    data: data
                }).done(function (response) {
                    done(response || {});
                }).fail(function () {
                    message(esc(text('failed')), 'text-danger');
                }).always(function () {
                    busy = false;
                    $('#destinationAdviceAsk, #destinationAdviceApply, #destinationAdviceDismiss').prop('disabled', false);
                    $('#destinationAdviceAsk span').text(text('ask'));
                    // Someone else changed the advice while this action ran: show it now, whether the action worked or failed.
                    if (refreshPending) {
                        refreshPending = false;
                        load();
                    }
                });
            }

            function ask() {
                $('#destinationAdviceAsk span').text(text('asking'));
                message(null);
                post('Ask', { callId: callId }, function (response) {
                    if (response.advice || response.success) {
                        render({ available: true, canApply: true, advice: response.advice });
                    }
                    if (response.outcome && response.outcome !== 'Advised') {
                        var note = outcomeText(response.outcome);
                        if (note) message(esc(note), response.success ? 'text-muted' : 'text-warning');
                    }
                });
            }

            function apply() {
                if (!current || !current.recommendedPoiId) return;
                var poiId = current.recommendedPoiId, poiName = current.recommendedPoiName;
                message(null);
                post('Apply', { adviceId: current.id }, function (response) {
                    if (!response.success) {
                        message(esc(outcomeText(response.outcome) || text('failed')), 'text-warning');
                        if (response.advice) render({ available: true, canApply: true, advice: response.advice.state === 1 ? response.advice : null });
                        return;
                    }

                    message(esc(format(text('applied'), poiName || ('#' + poiId))), 'text-success');
                    $('#destinationAdviceActions').hide();
                    if (panel.data('mode') === 'form') {
                        // Update Call: the form's destination field must carry the applied destination, or saving the form would put the old one back.
                        var select = $('#Call_DestinationPoiId');
                        if (select.length) {
                            if (!select.find('option[value="' + poiId + '"]').length) {
                                select.append($('<option></option>').attr('value', poiId).text(poiName || ('#' + poiId)));
                            }
                            select.val(String(poiId)).trigger('change');
                        }
                    } else {
                        // View Call shows the destination from the server; reload to show the new one.
                        setTimeout(function () { window.location.reload(); }, 900);
                    }
                });
            }

            function dismiss() {
                if (!current) return;
                var reason = window.prompt(text('dismissReason'), '');
                if (reason === null) return;
                message(null);
                post('Dismiss', { adviceId: current.id, reason: reason }, function (response) {
                    if (!response.success) {
                        message(esc(outcomeText(response.outcome) || text('failed')), 'text-warning');
                        return;
                    }

                    message(esc(text('dismissed')), 'text-muted');
                    render({ available: true, canApply: canApply, advice: null });
                });
            }

            function init() {
                panel = $('#destinationAdvicePanel');
                if (!panel.length) return;

                callId = parseInt(panel.data('call-id'), 10);
                if (!callId) return;

                $('#destinationAdviceAsk').on('click', ask);
                $('#destinationAdviceApply').on('click', apply);
                $('#destinationAdviceDismiss').on('click', dismiss);

                document.addEventListener('resgrid:ai-destination-advice-updated', function (e) {
                    if (e && e.detail && Number(e.detail.callId) === callId) scheduleLoad();
                });

                // Pages without their own eventing connection (Update Call) start one; View Call's script already has.
                if ((panel.data('start-hub') === true || panel.data('start-hub') === 'true') && resgrid.common && resgrid.common.signalr) {
                    resgrid.common.signalr.init(null, null, null, null);
                }

                load();
            }

            $(document).ready(function () {
                init();
            });
        })(destinationadvice = dispatch.destinationadvice || (dispatch.destinationadvice = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
