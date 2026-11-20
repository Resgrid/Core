var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var intakeconversation;
        (function (intakeconversation) {
            // Intake SMS conversation panel (_IntakeConversation.cshtml; call-intake-and-ai-destinations-plan.md section 8): the thread with
            // the member of the public who texted the department's dedicated number, Take / Release / End and a send box. Everything shown
            // comes from User/IntakeConversations, which applies the member's right to view and edit the call; message text is rendered as
            // plain text. Reloads on the intakeConversationUpdated eventing event for this call.
            var panel, callId, current = null, busy = false, refreshTimer = null, maxLength = 640;

            function strings() { return window.intakeConversationStrings || {}; }
            function text(key, fallback) { var s = strings(); return s[key] || fallback || ''; }
            function format(template, value) { return String(template).replace('{0}', value); }
            function esc(value) {
                if (resgrid.escapeHtml) return resgrid.escapeHtml(value == null ? '' : String(value));
                return $('<div>').text(value == null ? '' : String(value)).html();
            }
            function token() {
                var meta = document.querySelector('meta[name="request-verification-token"]');
                if (meta && meta.content) return meta.content;
                return $('input[name="__RequestVerificationToken"]').first().val();
            }
            function outcomeText(outcome) {
                var outcomes = strings().outcomes || {};
                var value = outcomes[outcome] || '';
                return outcome === 'InvalidMessage' ? format(value, maxLength) : value;
            }
            function notice(message, css) {
                var box = $('#intakeConversationNotice');
                if (!message) { box.hide().empty(); return; }
                box.attr('class', 'small ' + (css || 'text-muted')).text(message).show();
            }

            function load() {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/IntakeConversations/Thread',
                    data: { callId: callId },
                    type: 'GET',
                    cache: false
                }).done(function (response) {
                    render(response);
                }).fail(function () {
                    // A failed refresh keeps what is shown.
                });
            }
            intakeconversation.load = load;

            function scheduleLoad() {
                if (refreshTimer) clearTimeout(refreshTimer);
                refreshTimer = setTimeout(function () { refreshTimer = null; if (!busy) load(); }, 400);
            }

            function authorLabel(message) {
                switch (message.author) {
                    case 1: return text('authorCaller', 'Caller');
                    case 2: return text('authorAssistant', 'AI assistant');
                    case 3: return message.authorName || text('authorDispatcher', 'Dispatcher');
                    default: return text('authorSystem', 'Automatic message');
                }
            }

            function statusLabel(message) {
                if (message.direction !== 2) return '';
                if (message.status === 4) return '<span class="text-danger">' + esc(text('statusFailed')) + '</span>';
                if (message.status === 2) return esc(text('statusQueued'));
                if (message.status === 3) return esc(text('statusSent'));
                return '';
            }

            function bubble(message) {
                var inbound = message.direction === 1;
                var when = message.createdOn ? new Date(message.createdOn) : null;
                var time = when && !isNaN(when.getTime()) ? when.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '';
                var status = statusLabel(message);
                var body = message.text == null || message.text === ''
                    ? '<span class="text-muted">' + esc(text('textUnavailable')) + '</span>'
                    : esc(message.text);
                return '<div style="text-align: ' + (inbound ? 'left' : 'right') + ';">' +
                    '<div style="display: inline-block; max-width: 85%; text-align: left; margin: 3px 0; padding: 5px 9px; border-radius: 6px; background: ' +
                    (inbound ? '#f3f3f4' : '#e6f1fb') + ';">' +
                    '<div class="small text-muted">' + esc(authorLabel(message)) + (time ? ' &middot; ' + esc(time) : '') + (status ? ' &middot; ' + status : '') + '</div>' +
                    '<div style="white-space: pre-wrap; word-break: break-word;">' + body + '</div>' +
                    '</div></div>';
            }

            function handlerText(conversation) {
                if (conversation.state !== 1) {
                    var reasons = strings().closeReasons || {};
                    var reason = conversation.closeReason ? (reasons[conversation.closeReason] || conversation.closeReason) : '';
                    return reason ? format(text('closedReason'), reason) : '';
                }
                if (conversation.handler === 1) return text('handlerAssistant');
                if (conversation.handler === 3) {
                    return conversation.isMine ? text('handlerDispatcherYou')
                        : format(text('handlerDispatcher'), conversation.assignedName || text('authorDispatcher', 'Dispatcher'));
                }
                return text('handlerUnassisted');
            }

            function render(response) {
                if (!response || response.exists === false || !response.conversation) {
                    if (response && response.outcome) notice(outcomeText(response.outcome), 'text-warning');
                    if (!current) panel.hide();
                    return;
                }

                panel.show();
                current = response;
                if (response.maxLength) maxLength = response.maxLength;
                var conversation = response.conversation;
                var open = conversation.state === 1;

                $('#intakeConversationState').attr('class', 'label ' + (open ? 'label-primary' : 'label-default'))
                    .text(open ? text('stateOpen') : text('stateClosed'));
                $('#intakeConversationHandler').attr('class', 'small ' + (open && conversation.handler === 2 ? 'text-warning' : 'text-muted'))
                    .text(handlerText(conversation));
                $('#intakeConversationTexter').text(conversation.participant ? format(text('texter'), conversation.participant) : '');

                var thread = $('#intakeConversationThread');
                var messages = response.messages || [];
                thread.html(messages.length ? messages.map(bubble).join('') : '<span class="text-muted small">' + esc(text('noMessages')) + '</span>');
                thread.scrollTop(thread.prop('scrollHeight'));

                var canAct = !!response.canConnect && open;
                var heldByOther = !!conversation.assignedUserId && !conversation.isMine;
                $('#intakeConversationActions').toggle(canAct);
                $('#intakeConversationTake').toggle(canAct && !conversation.isMine);
                $('#intakeConversationRelease').toggle(canAct && !!conversation.isMine);
                // Only the connected dispatcher texts; with nobody connected, sending connects first.
                $('#intakeConversationCompose').toggle(canAct && !heldByOther);

                if (conversation.optedOut) notice(text('optedOut'), 'text-warning');
            }

            function post(action, data, done) {
                if (busy || !current || !current.conversation) return;
                busy = true;
                data.conversationId = current.conversation.id;
                data.__RequestVerificationToken = token();
                $('#intakeConversationPanel button').prop('disabled', true);
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/IntakeConversations/' + action,
                    type: 'POST',
                    data: data
                }).done(function (response) {
                    response = response || {};
                    notice(null);
                    render(response);
                    if (response.outcome && response.outcome !== 'Done') {
                        notice(outcomeText(response.outcome) || text('failed'), 'text-warning');
                    } else if (done) {
                        done(response);
                    }
                }).fail(function () {
                    notice(text('failed'), 'text-danger');
                }).always(function () {
                    busy = false;
                    $('#intakeConversationPanel button').prop('disabled', false);
                });
            }

            function take() {
                var conversation = current && current.conversation;
                if (!conversation) return;
                var takeOver = !!conversation.assignedUserId && !conversation.isMine;
                if (takeOver && !window.confirm(text('takeOverConfirm'))) return;
                post('Connect', { takeOver: takeOver });
            }

            function release() {
                post('Release', {});
            }

            function close() {
                if (!window.confirm(text('closeConfirm'))) return;
                post('Close', { notify: $('#intakeConversationNotify').is(':checked') });
            }

            function updateCounter() {
                var length = ($('#intakeConversationText').val() || '').length;
                $('#intakeConversationCounter').text(length + '/' + maxLength).toggleClass('text-danger', length > maxLength);
            }

            function send() {
                var box = $('#intakeConversationText');
                var value = (box.val() || '').trim();
                if (!value || value.length > maxLength) {
                    notice(outcomeText('InvalidMessage'), 'text-warning');
                    return;
                }
                post('Send', { text: value }, function () {
                    box.val('');
                    updateCounter();
                });
            }

            function init() {
                panel = $('#intakeConversationPanel');
                if (!panel.length) return;

                callId = parseInt(panel.data('call-id'), 10);
                if (!callId) return;
                maxLength = parseInt(panel.data('max-length'), 10) || maxLength;

                $('#intakeConversationTake').on('click', take);
                $('#intakeConversationRelease').on('click', release);
                $('#intakeConversationClose').on('click', close);
                $('#intakeConversationSend').on('click', send);
                $('#intakeConversationText').on('input', updateCounter).on('keydown', function (e) {
                    // Enter sends; Shift+Enter starts a new line; an IME composition is never cut short.
                    if (e.key === 'Enter' && !e.shiftKey && !e.isComposing && !(e.originalEvent && e.originalEvent.isComposing)) {
                        e.preventDefault();
                        send();
                    }
                });

                document.addEventListener('resgrid:intake-conversation-updated', function (e) {
                    if (e && e.detail && Number(e.detail.callId) === callId) scheduleLoad();
                });

                // Pages without their own eventing connection (Update Call) start one, unless the destination advice panel already did.
                var adviceStartsHub = $('#destinationAdvicePanel').length &&
                    ($('#destinationAdvicePanel').data('start-hub') === true || $('#destinationAdvicePanel').data('start-hub') === 'true');
                if ((panel.data('start-hub') === true || panel.data('start-hub') === 'true') && !adviceStartsHub && resgrid.common && resgrid.common.signalr) {
                    resgrid.common.signalr.init(null, null, null, null);
                }

                updateCounter();
                load();
            }

            $(document).ready(function () {
                init();
            });
        })(intakeconversation = dispatch.intakeconversation || (dispatch.intakeconversation = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
