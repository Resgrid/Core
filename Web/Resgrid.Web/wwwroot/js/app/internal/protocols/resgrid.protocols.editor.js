
var resgrid;
(function (resgrid) {
    var protocols;
    (function (protocols) {
        var editor;
        (function (editor) {
            // The protocol editor (New and Edit). Rows are posted with the "Index" convention, so adding a row is cloning a
            // template with a fresh key and removing one is taking it out of the form; nothing is renumbered.
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
                $trigger.find('.protocol-trigger-priority').prop('hidden', type === 1);
                $trigger.find('.protocol-trigger-calltype').prop('hidden', type === 0);
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

            $(document).ready(function () {
                resgrid.common.analytics.track('Protocols - Editor');

                var quillDescription = new Quill('#editor-container', { placeholder: '', theme: 'snow' });
                var quillProtocolText = new Quill('#editor-container2', { placeholder: '', theme: 'snow' });

                $('#protocolEditorForm').on('submit', function () {
                    // An untouched editor posts "<p><br></p>": send an empty value instead.
                    var description = quillDescription.getText().trim().length ? quillDescription.root.innerHTML : '';
                    var text = quillProtocolText.getText().trim().length ? quillProtocolText.root.innerHTML : '';
                    $('#Description').val(description);
                    $('#ProtocolText').val(text);
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
            });
        })(editor = protocols.editor || (protocols.editor = {}));
    })(protocols = resgrid.protocols || (resgrid.protocols = {}));
})(resgrid || (resgrid = {}));
