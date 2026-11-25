
var resgrid;
(function (resgrid) {
    var protocols;
    (function (protocols) {
        var editor;
        (function (editor) {
            // The protocol editor (the creation wizard and the full-page editor). Rows are posted with the "Index" convention, so
            // adding a row is cloning a template with a fresh key and removing one is taking it out of the form; nothing is
            // renumbered. The full-page editor autosaves the draft text (P1, R-P8): every 30 seconds while it changed, sending the
            // draft's row version and keeping the new one, so a save over someone else's is refused and shown instead.
            var nextKey = 0;

            function freshKey(prefix) {
                nextKey++;
                return prefix + 'n' + Date.now().toString(36) + nextKey;
            }

            function fromTemplate(id, replacements) {
                var html = document.getElementById(id).innerHTML;
                Object.keys(replacements).forEach(function (token) {
                    html = html.split(token).join(replacements[token]);
                });
                return $(html.trim());
            }

            function showTriggerFields($trigger) {
                var type = Number($trigger.find('.protocol-trigger-type').val());
                // 0 priority, 1 type, 2 priority and type.
                $trigger.find('.protocol-trigger-priority').prop('hidden', type === 1 || type === 3);
                $trigger.find('.protocol-trigger-calltype').prop('hidden', type === 0 || type === 3);
                // P4: an "any call" trigger is decided by its conditions alone; open them.
                if (type === 3) $trigger.find('.protocol-trigger-conditions').prop('open', true);
            }

            function refreshEmptyTexts() {
                $('#noTriggersText').prop('hidden', $('#triggerRows .protocol-trigger').length > 0);
                $('#noQuestionsText').prop('hidden', $('#questionRows .protocol-question').length > 0);
            }

            function addAnswer($question) {
                var qkey = $question.data('key');
                var $answer = fromTemplate('answerTemplate', { '__qkey__': qkey, '__akey__': freshKey('a') });
                $question.find('.protocol-answers').append($answer);
                $answer.find('input[type=text], input:not([type])').first().trigger('focus');
            }

            // Dispatch (0) and Procedure (4) protocols are matched to calls; the other kinds have no triggers or questions.
            function showKindSections() {
                var $kind = $('.protocol-kind:checked, select.protocol-kind').first();
                if (!$kind.length) {
                    return;
                }
                var kind = Number($kind.val());
                var dispatch = kind === 0 || kind === 4;
                $('.protocol-dispatch-only').prop('hidden', !dispatch);
                $('.protocol-dispatch-off').prop('hidden', dispatch);
                // Policy (3) and SOG (1) versions require acknowledgment by default.
                var $ack = $('#Library_RequiresAcknowledgment');
                if ($ack.length && !$ack.data('touched')) {
                    $ack.prop('checked', kind === 1 || kind === 3);
                }
            }

            function showAudience() {
                var everyone = $('.protocol-audience-mode:checked').val() !== 'false';
                $('.protocol-audience-rules').prop('hidden', everyone);
            }

            function texts() {
                try {
                    return JSON.parse($('#protocolEditorText').text() || '{}');
                } catch (e) {
                    return {};
                }
            }

            $(document).ready(function () {
                resgrid.common.analytics.track('Protocols - Editor');

                var quillDescription = document.getElementById('editor-container') ? new Quill('#editor-container', { placeholder: '', theme: 'snow' }) : null;
                var quillProtocolText = document.getElementById('editor-container2')
                    ? new Quill('#editor-container2', { placeholder: '', theme: 'snow', modules: { toolbar: [[{ header: [2, 3, 4, false] }], ['bold', 'italic', 'underline'], [{ list: 'ordered' }, { list: 'bullet' }], ['link'], ['clean']] } })
                    : null;

                function syncEditors() {
                    // An untouched editor posts "<p><br></p>": send an empty value instead.
                    if (quillDescription) {
                        $('#Description').val(quillDescription.getText().trim().length ? quillDescription.root.innerHTML : '');
                    }
                    if (quillProtocolText) {
                        $('#ProtocolText').val(quillProtocolText.getText().trim().length ? quillProtocolText.root.innerHTML : '');
                    }
                }

                $('#protocolEditorForm').on('submit', function () {
                    syncEditors();
                    return true;
                });

                $('#triggerRows .protocol-trigger').each(function () { showTriggerFields($(this)); });

                $('#addTriggerButton').on('click', function () {
                    var $trigger = fromTemplate('triggerTemplate', { '__key__': freshKey('t') });
                    $('#triggerRows').append($trigger);
                    showTriggerFields($trigger);
                    refreshEmptyTexts();
                    $trigger.find('.protocol-trigger-type').trigger('focus');
                });

                $('#addQuestionButton').on('click', function () {
                    var $question = fromTemplate('questionTemplate', { '__key__': freshKey('q') });
                    $('#questionRows').append($question);
                    addAnswer($question);
                    addAnswer($question);
                    refreshEmptyTexts();
                    $question.find('textarea').trigger('focus');
                });

                $(document).on('change', '.protocol-trigger-type', function () {
                    showTriggerFields($(this).closest('.protocol-trigger'));
                });

                $(document).on('click', '.protocol-add-answer', function () {
                    addAnswer($(this).closest('.protocol-question'));
                });

                $(document).on('click', '.protocol-remove-answer', function () {
                    $(this).closest('.protocol-answer').remove();
                });

                $(document).on('click', '.protocol-remove-row', function () {
                    $(this).closest('.protocol-trigger, .protocol-question').remove();
                    refreshEmptyTexts();
                    $('#addTriggerButton').trigger('focus');
                });

                $(document).on('change', '.protocol-kind', showKindSections);
                $('#Library_RequiresAcknowledgment').on('change', function () { $(this).data('touched', true); });
                $(document).on('change', '.protocol-audience-mode', showAudience);
                showAudience();

                // Autosave of the draft text (the full-page editor only).
                var $form = $('#protocolEditorForm');
                var autosaveUrl = $form.data('autosave-url');
                if (autosaveUrl && quillProtocolText && $form.data('content-locked') !== true && $form.data('content-locked') !== 'true') {
                    var text = texts();
                    var dirty = false;
                    var saving = false;
                    var $status = $('#autosaveStatus');
                    quillProtocolText.on('text-change', function () { dirty = true; });
                    // P4: the case flow builder's graph is draft content too.
                    $(document).on('resgrid:graph-changed', function () { dirty = true; });
                    $('#Library_ChangeSummary').on('input', function () { dirty = true; });

                    window.setInterval(function () {
                        if (!dirty || saving || !autosaveUrl) {
                            return;
                        }
                        saving = true;
                        dirty = false;
                        syncEditors();
                        $status.text(text.saving || '');
                        $.ajax({
                            url: autosaveUrl,
                            type: 'POST',
                            data: {
                                protocolText: $('#ProtocolText').val(),
                                decisionGraphJson: $('#DecisionGraphJson').length ? $('#DecisionGraphJson').val() : undefined,
                                changeSummary: $('#Library_ChangeSummary').val(),
                                requiresAcknowledgment: $('#Library_RequiresAcknowledgment').is(':checked'),
                                draftRowVersion: $('#DraftRowVersion').val(),
                                __RequestVerificationToken: $form.find('input[name="__RequestVerificationToken"]').val()
                            }
                        }).done(function (result) {
                            if (result && result.success) {
                                $('#DraftRowVersion').val(result.rowVersion);
                                $status.removeClass('text-danger').addClass('text-muted').text((text.saved || '') + ' ' + new Date().toLocaleTimeString());
                            } else {
                                // A stale copy stops autosave: the author must reload to see the other person's changes.
                                $status.removeClass('text-muted').addClass('text-danger').text((result && result.message) || text.failed || '');
                                if (result && result.error === 'StaleCopy') {
                                    autosaveUrl = null;
                                }
                            }
                        }).fail(function () {
                            dirty = true;
                            $status.removeClass('text-muted').addClass('text-danger').text(text.failed || '');
                        }).always(function () {
                            saving = false;
                        });
                    }, 30000);
                }
            });
        })(editor = protocols.editor || (protocols.editor = {}));
    })(protocols = resgrid.protocols || (resgrid.protocols = {}));
})(resgrid || (resgrid = {}));
