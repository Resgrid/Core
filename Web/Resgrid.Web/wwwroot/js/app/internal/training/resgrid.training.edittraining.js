var resgrid;
(function (resgrid) {
    var training;
    (function (training) {
        var edittraining;
        (function (edittraining) {
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
                });

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

                initSelect2('#groupsToAdd', 'Select groups...', resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=1');
                initSelect2('#rolesToAdd', 'Select roles...', resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=2');
                initSelect2('#usersToAdd', 'Select users...', resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterSelf=true');
                resgrid.training.edittraining.questionsCount = 0;
            });
            function addQuestion() {
                resgrid.training.edittraining.questionsCount++;
                $('#questions tbody').first().append("<tr><td style='max-width: 215px;'><textarea id='question_" + edittraining.questionsCount + "' name='question_" + edittraining.questionsCount + "' rows='4' cols='40'></textarea></td><td>" + resgrid.training.edittraining.generateAnswersTable(edittraining.questionsCount) + "</td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='tip-top' data-original-title='Remove this question'><i class='fa fa-minus' style='color: red;'></i></a></td></tr>");
            }
            edittraining.addQuestion = addQuestion;
            function generateAnswersTable(count) {
                var answersTable = '<table id="answersTable_' + count + '" class="table table-striped table-bordered"><thead><tr><th style="max-width:35px;font-size: 14px;" >Ans</th><th style = "font-size: 14px;" >Answer Text</th><th style = "font-size: 16px;" ><a id="addAnswerButton" class="btn btn-success btn-xs" onclick="resgrid.training.edittraining.addAnswer(' + count + ');" data-original-title="Add Answers to Question" ><i class="icon-plus" ></i> Add Answer</a></th></tr></thead><tbody></tbody></table>';
                return answersTable;
            }
            edittraining.generateAnswersTable = generateAnswersTable;
            function addAnswer(count) {
                var id = generate(4);
                $('#answersTable_' + count + ' tbody').append("<tr><td><input type='radio' name='answer_" + count + "' value='answerForQuestion_" + count + "_" + id + "'></td><td><textarea rows='3' cols='30' data-bv-notempty data-bv-notempty-message='Answer is required' id='answerForQuestion_" + count + "_" + id + "' name='answerForQuestion_" + count + "_" + id + "'></textarea></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='tip-top' data-original-title='Remove this answer from the question'><i class='fa fa-minus' style='color: red;'></i></a></td></tr>");
            }
            edittraining.addAnswer = addAnswer;
            function removeQuestion(index) {
                $('#questionRow_' + index).remove();
            }
            edittraining.removeQuestion = removeQuestion;
            function generate(length) {
                var arr = [];
                var n;
                for (var i = 0; i < length; i++) {
                    do
                        n = Math.floor(Math.random() * 20 + 1);
                    while (arr.indexOf(n) !== -1);
                    arr[i] = n;
                }
                return arr.join('');
            }
            edittraining.generate = generate;
        })(edittraining = training.edittraining || (training.edittraining = {}));
    })(training = resgrid.training || (resgrid.training = {}));
})(resgrid || (resgrid = {}));