
var resgrid;
(function (resgrid) {
    var training;
    (function (training) {
        var edittraining;
        (function (edittraining) {
            var i18n = (typeof resgridTrainingsI18n !== 'undefined') ? resgridTrainingsI18n : {};

            function escapeHtml(str) {
                if (str === null || str === undefined) return '';
                return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
            }

            function inputTypeFor(type) {
                return String(type) === '1' ? 'checkbox' : 'radio';
            }

            function answerRow(n, m, type, text, readOnly) {
                return '<tr>' +
                    '<td><input type="' + inputTypeFor(type) + '" name="correct_' + n + '" value="' + m + '" aria-label="' + escapeHtml(i18n.correct) + '" /></td>' +
                    '<td><input type="hidden" name="answerId_' + n + '_' + m + '" value="0" />' +
                    '<input type="text" class="form-control" name="answerForQuestion_' + n + '_' + m + '" value="' + escapeHtml(text || '') + '"' + (readOnly ? ' readonly' : '') + ' aria-label="' + escapeHtml(i18n.answerText) + '" /></td>' +
                    '<td class="rgw-row-actions">' + (readOnly ? '' : '<button type="button" class="btn btn-xs btn-default training-remove-answer"><i class="fa fa-minus" aria-hidden="true"></i> ' + escapeHtml(i18n.removeAnswer) + '</button>') + '</td>' +
                    '</tr>';
            }

            function addQuestion() {
                var button = $('#addQuestionButton');
                var n = parseInt(button.attr('data-next-question'), 10) || 1;
                button.attr('data-next-question', n + 1);

                var heading = escapeHtml(String(i18n.questionNumber || '{0}').replace('{0}', n));
                var html = '<fieldset class="rgw-builder-section training-question" data-question="' + n + '" data-next-answer="1">' +
                    '<legend class="rgw-builder-heading">' + heading + '</legend>' +
                    '<input type="hidden" name="questionId_' + n + '" value="0" />' +
                    '<div class="form-group"><label for="question_' + n + '">' + escapeHtml(i18n.question) + '</label>' +
                    '<textarea id="question_' + n + '" name="question_' + n + '" class="form-control" rows="2"></textarea></div>' +
                    '<div class="form-group"><label for="questionType_' + n + '">' + escapeHtml(i18n.questionType) + '</label>' +
                    '<select id="questionType_' + n + '" name="questionType_' + n + '" class="form-control training-question-type">' +
                    '<option value="0">' + escapeHtml(i18n.typeSingle) + '</option>' +
                    '<option value="1">' + escapeHtml(i18n.typeMulti) + '</option>' +
                    '<option value="2">' + escapeHtml(i18n.typeTrueFalse) + '</option>' +
                    '</select></div>' +
                    '<table class="table table-condensed rgw-table training-answers"><thead><tr><th>' + escapeHtml(i18n.correct) + '</th><th>' + escapeHtml(i18n.answerText) +
                    '</th><th class="rgw-row-actions"><span class="sr-only">' + escapeHtml(i18n.actions) + '</span></th></tr></thead><tbody></tbody></table>' +
                    '<div class="rgw-builder-actions">' +
                    '<button type="button" class="btn btn-xs btn-success training-add-answer"><i class="fa fa-plus" aria-hidden="true"></i> ' + escapeHtml(i18n.addAnswer) + '</button> ' +
                    '<button type="button" class="btn btn-xs btn-danger training-remove-question"><i class="fa fa-trash" aria-hidden="true"></i> ' + escapeHtml(i18n.removeQuestion) + '</button>' +
                    '</div></fieldset>';

                var fieldset = $(html).appendTo('#questions');
                addAnswer(fieldset);
                addAnswer(fieldset);
                fieldset.find('textarea').first().focus();
            }
            edittraining.addQuestion = addQuestion;

            function addAnswer(fieldset, text, readOnly) {
                var n = fieldset.attr('data-question');
                var m = parseInt(fieldset.attr('data-next-answer'), 10) || 1;
                fieldset.attr('data-next-answer', m + 1);
                var type = fieldset.find('.training-question-type').val();
                fieldset.find('.training-answers tbody').append(answerRow(n, m, type, text, readOnly));
            }
            edittraining.addAnswer = addAnswer;

            // True/false: exactly two fixed options, one correct; multiple choice: check boxes; single: radios.
            function applyType(fieldset) {
                var type = String(fieldset.find('.training-question-type').val());
                var tbody = fieldset.find('.training-answers tbody');

                if (type === '2') {
                    tbody.empty();
                    addAnswer(fieldset, i18n.trueLabel || 'True', true);
                    addAnswer(fieldset, i18n.falseLabel || 'False', true);
                    fieldset.find('.training-add-answer').hide();
                    return;
                }

                fieldset.find('.training-add-answer').show();
                fieldset.find('.training-answers input[readonly]').prop('readonly', false);
                fieldset.find('input[name="correct_' + fieldset.attr('data-question') + '"]').each(function () {
                    var current = $(this);
                    if (current.attr('type') !== inputTypeFor(type)) {
                        var replacement = $('<input />').attr({ type: inputTypeFor(type), name: current.attr('name'), value: current.val(), 'aria-label': current.attr('aria-label') });
                        replacement.prop('checked', current.prop('checked') && type === '1');
                        current.replaceWith(replacement);
                    }
                });
            }

            /* An accepted drafted question: a single-choice question with its options and the correct one ticked. */
            function acceptQuestion(question) {
                if (!question || !question.text || !question.options || question.options.length < 2) {
                    return;
                }
                addQuestion();
                var fieldset = $('#questions .training-question').last();
                fieldset.find('textarea').first().val(question.text);
                fieldset.find('.training-question-type').val('0');
                fieldset.find('.training-answers tbody').empty();
                question.options.forEach(function (option) { addAnswer(fieldset, option, false); });
                var n = fieldset.attr('data-question');
                fieldset.find('input[name="correct_' + n + '"]').eq(question.correctIndex).prop('checked', true);
            }

            /* An accepted drafted outline, appended to the training text as headings and lists (text only, never markup from the draft). */
            function acceptOutline(editor, modules) {
                var index = Math.max(0, editor.getLength() - 1);
                modules.forEach(function (module) {
                    if (!module || !module.title) {
                        return;
                    }
                    editor.insertText(index, module.title + '\n', { header: 3 });
                    index = Math.max(0, editor.getLength() - 1);
                    if (module.objective) {
                        editor.insertText(index, module.objective + '\n');
                        index = Math.max(0, editor.getLength() - 1);
                    }
                    (module.lessons || []).forEach(function (lesson) {
                        editor.insertText(index, lesson + '\n', { list: 'ordered' });
                        index = Math.max(0, editor.getLength() - 1);
                    });
                });
            }

            function syncDueMode() {
                var mode = $('#DueMode').val();
                $('.training-due-fixed').toggle(mode === 'fixed');
                $('.training-due-relative').toggle(mode === 'relative');
            }

            function syncOpen() {
                $('.training-self-enroll').toggle($('#OpenToAllMembers').is(':checked'));
            }

            function syncEveryone() {
                var everyone = $('#AssignEveryone').is(':checked');
                $('.training-audience select').prop('disabled', everyone).trigger('change.select2');
            }

            $(document).ready(function () {
                resgrid.common.analytics.track('Training - Edit');

                var toolbar = [[{ header: [2, 3, false] }], ['bold', 'italic', 'underline', 'strike'], [{ list: 'ordered' }, { list: 'bullet' }], ['blockquote', 'link', 'video'], ['clean']];
                var description = $('#description-editor').length ? new Quill('#description-editor', { theme: 'snow', modules: { toolbar: toolbar } }) : null;
                var text = $('#text-editor').length ? new Quill('#text-editor', { theme: 'snow', modules: { toolbar: toolbar } }) : null;

                $('#trainingEditorForm').on('submit', function () {
                    if (description) $('#Description').val(description.root.innerHTML);
                    if (text) $('#TrainingText').val(text.root.innerHTML);
                    $('.training-audience select').prop('disabled', false);
                    return true;
                });

                // K2 drafting aids: what the author accepted lands in the editor; nothing is saved until the training is.
                document.addEventListener('rg:authoring-aid:accept', function (event) {
                    var detail = event.detail || {};
                    if (detail.kind === 'quiz' && detail.questions) {
                        detail.questions.forEach(acceptQuestion);
                    } else if (detail.kind === 'outline' && detail.modules && text) {
                        acceptOutline(text, detail.modules);
                    }
                });

                $('#addQuestionButton').on('click', function () { addQuestion(); });
                $('#questions').on('click', '.training-add-answer', function () { addAnswer($(this).closest('.training-question')); });
                $('#questions').on('click', '.training-remove-answer', function () { $(this).closest('tr').remove(); });
                $('#questions').on('click', '.training-remove-question', function () { $(this).closest('.training-question').remove(); });
                $('#questions').on('change', '.training-question-type', function () { applyType($(this).closest('.training-question')); });
                $('#questions .training-question').each(function () {
                    if (String($(this).find('.training-question-type').val()) === '2') {
                        $(this).find('.training-add-answer').hide();
                    }
                });

                $('#DueMode').on('change', syncDueMode);
                $('#OpenToAllMembers').on('change', syncOpen);
                $('#AssignEveryone').on('change', syncEveryone);
                syncDueMode();
                syncOpen();

                if ($.fn.select2) {
                    $('.training-select').each(function () {
                        $(this).select2({ placeholder: $(this).attr('data-placeholder') || '', allowClear: true, width: '100%' });
                    });
                }
                syncEveryone();
            });
        })(edittraining = training.edittraining || (training.edittraining = {}));
    })(training = resgrid.training || (resgrid.training = {}));
})(resgrid || (resgrid = {}));
