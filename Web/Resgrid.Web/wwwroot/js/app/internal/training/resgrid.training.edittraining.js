var resgrid;
(function (resgrid) {
    var training;
    (function (training) {
        var edittraining;
        (function (edittraining) {
            var i18n = (typeof resgridTrainingsI18n !== 'undefined') ? resgridTrainingsI18n : {};

            function escapeHtml(str) {
                if (!str) return '';
                return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
            }

            $(document).ready(function () {
                resgrid.common.analytics.track('Training - Edit');

                var quillDescription = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                var quillTraining = new Quill('#editor-container2', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#editTrainingForm', function () {
                    $('#Training_Description').val(quillDescription.root.innerHTML);
                    $('#Training_TrainingText').val(quillTraining.root.innerHTML);

                    return true;
                });

                // Date picker - no time needed
                $('#Training_ToBeCompletedBy').datetimepicker({
                    timepicker: false,
                    format: 'm/d/Y',
                    scrollMonth: false,
                    scrollInput: false
                });

                // File upload: use native HTML file input (no Kendo Upload needed)

                $('#SendToAll').change(function () {
                    if (this.checked) {
                        $('#groupsToAdd').prop('disabled', true).trigger('change.select2');
                        $('#rolesToAdd').prop('disabled', true).trigger('change.select2');
                        $('#usersToAdd').prop('disabled', true).trigger('change.select2');
                    } else {
                        $('#groupsToAdd').prop('disabled', false).trigger('change.select2');
                        $('#rolesToAdd').prop('disabled', false).trigger('change.select2');
                        $('#usersToAdd').prop('disabled', false).trigger('change.select2');
                    }
                }).trigger('change');

                function initSelect2(selector, placeholder, url) {
                    $(selector).select2({
                        placeholder: placeholder,
                        allowClear: true,
                        ajax: {
                            url: url,
                            dataType: 'json',
                            processResults: function (data) {
                                return {
                                    results: $.map(data, function (item) {
                                        return { id: item.Id, text: item.Name };
                                    })
                                };
                            }
                        }
                    });
                }

                initSelect2('#groupsToAdd', i18n.selectGroups || 'Select groups...', resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=1');
                initSelect2('#rolesToAdd', i18n.selectRoles || 'Select roles...', resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=2');
                initSelect2('#usersToAdd', i18n.selectUsers || 'Select users...', resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterSelf=true');
                // Derive questionsCount from existing server-rendered question rows to avoid index collisions
                var maxIndex = 0;
                $('#questions tbody').first().find('input[name^="question_"], textarea[name^="question_"]').each(function () {
                    var match = $(this).attr('name').match(/^question_(\d+)$/);
                    if (match) {
                        var idx = parseInt(match[1], 10);
                        if (idx > maxIndex) maxIndex = idx;
                    }
                });
                resgrid.training.edittraining.questionsCount = maxIndex;
            });
            function addQuestion() {
                var removeTooltip = escapeHtml(i18n.removeQuestionTooltip || 'Remove this question');
                resgrid.training.edittraining.questionsCount++;
                $('#questions tbody').first().append("<tr id='questionRow_" + edittraining.questionsCount + "'><td style='max-width: 215px;'><textarea id='question_" + edittraining.questionsCount + "' name='question_" + edittraining.questionsCount + "' rows='4' cols='40'></textarea></td><td>" + resgrid.training.edittraining.generateAnswersTable(edittraining.questionsCount) + "</td><td style='text-align:center;'><a onclick='resgrid.training.edittraining.removeQuestion(" + edittraining.questionsCount + ");' class='tip-top' data-original-title='" + removeTooltip + "'><i class='fa fa-minus' style='color: red;'></i></a></td></tr>");
            }
            edittraining.addQuestion = addQuestion;
            function generateAnswersTable(count) {
                var addAnswerLabel = escapeHtml(i18n.addAnswer || 'Add Answer');
                var addAnswerTooltip = escapeHtml(i18n.addAnswerTooltip || 'Add Answers to Question');
                var correctLabel = escapeHtml(i18n.correct || 'Correct');
                var answerTextLabel = escapeHtml(i18n.answerText || 'Answer Text');
                var answersTable = '<table id="answersTable_' + count + '" class="table table-striped table-bordered"><thead><tr><th style="max-width:35px;font-size: 14px;" >' + correctLabel + '</th><th style = "font-size: 14px;" >' + answerTextLabel + '</th><th style = "font-size: 16px;" ><a id="addAnswerButton_' + count + '" class="btn btn-success btn-xs" onclick="resgrid.training.edittraining.addAnswer(' + count + ');" data-original-title="' + addAnswerTooltip + '" ><i class="icon-plus" ></i> ' + addAnswerLabel + '</a></th></tr></thead><tbody></tbody></table>';
                return answersTable;
            }
            edittraining.generateAnswersTable = generateAnswersTable;
            function addAnswer(count) {
                var id = generate(4);
                var answerRequired = escapeHtml(i18n.answerRequired || 'Answer is required');
                var removeAnswerTooltip = escapeHtml(i18n.removeAnswerTooltip || 'Remove this answer from the question');
                $('#answersTable_' + count + ' tbody').append("<tr><td><input type='radio' name='answer_" + count + "' value='answerForQuestion_" + count + "_" + id + "'></td><td><textarea rows='3' cols='30' data-bv-notempty data-bv-notempty-message='" + answerRequired + "' id='answerForQuestion_" + count + "_" + id + "' name='answerForQuestion_" + count + "_" + id + "'></textarea></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='tip-top' data-original-title='" + removeAnswerTooltip + "'><i class='fa fa-minus' style='color: red;'></i></a></td></tr>");
            }
            edittraining.addAnswer = addAnswer;
            function removeQuestion(index) {
                $('#questionRow_' + index).remove();
            }
            edittraining.removeQuestion = removeQuestion;
            var _answerIdCounter = 0;
            function generate() {
                return ++_answerIdCounter;
            }
            edittraining.generate = generate;
        })(edittraining = training.edittraining || (training.edittraining = {}));
    })(training = resgrid.training || (resgrid.training = {}));
})(resgrid || (resgrid = {}));
