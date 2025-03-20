
var resgrid;
(function (resgrid) {
    var training;
    (function (training) {
        var newtraining;
        (function (newtraining) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Training - New');

                var quillDescription = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                var quillTraining = new Quill('#editor-container2', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#newTrainingForm', function () {
                    $('#Training_Description').val(quillDescription.root.innerHTML);
                    $('#Training_TrainingText').val(quillTraining.root.innerHTML);

                    return true;
                });

                $("#Training_ToBeCompletedBy").kendoDatePicker();
                $("#attachments").kendoUpload();
                $('#SendToAll').change(function () {
                    if (this.checked) {
                        $('#groupsToAdd').data("kendoMultiSelect").enable(false);
                        $('#rolesToAdd').data("kendoMultiSelect").enable(false);
                        $('#usersToAdd').data("kendoMultiSelect").enable(false);
                    }
                    else {
                        $('#groupsToAdd').data("kendoMultiSelect").enable(true);
                        $('#rolesToAdd').data("kendoMultiSelect").enable(true);
                        $('#usersToAdd').data("kendoMultiSelect").enable(true);
                    }
                });
                $("#groupsToAdd").kendoMultiSelect({
                    placeholder: "Select groups...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=1'
                        }
                    }
                });
                $("#rolesToAdd").kendoMultiSelect({
                    placeholder: "Select roles...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=2'
                        }
                    }
                });
                $("#usersToAdd").kendoMultiSelect({
                    placeholder: "Select users...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterSelf=true'
                        }
                    }
                });
                resgrid.training.newtraining.questionsCount = 0;
            });
            function addQuestion() {
                resgrid.training.newtraining.questionsCount++;
                $('#questions tbody').first().append("<tr><td style='max-width: 215px;'><textarea id='question_" + newtraining.questionsCount + "' name='question_" + newtraining.questionsCount + "' rows='4' cols='40'></textarea></td><td>" + resgrid.training.newtraining.generateAnswersTable(newtraining.questionsCount) + "</td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='tip-top' data-original-title='Remove this question'><i class='fa fa-minus' style='color: red;'></i></a></td></tr>");
            }
            newtraining.addQuestion = addQuestion;
            function generateAnswersTable(count) {
                var answersTable = '<table id="answersTable_' + count + '" class="table table-striped table-bordered"><thead><tr><th style="max-width:35px;font-size: 14px;" >Ans</th><th style = "font-size: 14px;" >Answer Text</th><th style = "font-size: 16px;" ><a id="addAnswerButton" class="btn btn-success btn-xs" onclick="resgrid.training.newtraining.addAnswer(' + count + ');" data-original-title="Add Answers to Question" ><i class="icon-plus" ></i> Add Answer</a></th></tr></thead><tbody></tbody></table>';
                return answersTable;
            }
            newtraining.generateAnswersTable = generateAnswersTable;
            function addAnswer(count) {
                var id = generate(4);
                $('#answersTable_' + count + ' tbody').append("<tr><td><input type='radio' name='answer_" + count + "' value='answerForQuestion_" + count + "_" + id + "'></td><td><textarea rows='3' cols='30' data-bv-notempty data-bv-notempty-message='Answer is required' id='answerForQuestion_" + count + "_" + id + "' name='answerForQuestion_" + count + "_" + id + "'></textarea></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='tip-top' data-original-title='Remove this answer from the question'><i class='fa fa-minus' style='color: red;'></i></a></td></tr>");
                //addGroupRoleField('answerForQuestion_' + count + '_' + timestamp.getUTCMilliseconds());
            }
            newtraining.addAnswer = addAnswer;
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
            newtraining.generate = generate;
        })(newtraining = training.newtraining || (training.newtraining = {}));
    })(training = resgrid.training || (resgrid.training = {}));
})(resgrid || (resgrid = {}));
