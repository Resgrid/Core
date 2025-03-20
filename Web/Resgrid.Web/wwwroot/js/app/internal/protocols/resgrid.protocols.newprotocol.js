
var resgrid;
(function (resgrid) {
    var protocols;
    (function (protocols) {
        var newprotocol;
        (function (newprotocol) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Protocols - New');

                let quillDescription = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                let quillProtocolText = new Quill('#editor-container2', {
                    placeholder: '',
                    theme: 'snow'
                });

                $('#newTriggerStartsOn').datetimepicker({ step: 15 });
                $('#newTriggerEndsOn').datetimepicker({ step: 15 });

                $("#attachments").kendoUpload();

                $(document).on('submit', '#newProtocolForm', function () {
                    $('#Protocol_Description').val(quillDescription.root.innerHTML);
                    $('#Protocol_ProtocolText').val(quillProtocolText.root.innerHTML);

                    return true;
                });

                resgrid.protocols.newprotocol.triggerCount = 0;
                resgrid.protocols.newprotocol.questionsCount = 0;
            });
            function addQuestion() {
                resgrid.protocols.newprotocol.questionsCount++;

                $('#addQuestionModal').modal('hide');
                $('#questions tbody').first().append(`<tr>
                    <td style='max-width: 215px;'>${$('#newQuestion').val()}<input type='text' id='question_${newprotocol.questionsCount}' name='question_${newprotocol.questionsCount}' style='display:none;' value='${$('#newQuestion').val()}'></input></td>
                    <td>${resgrid.protocols.newprotocol.generateAnswersTable(newprotocol.questionsCount)}</td>"
                    <td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='tip-top' data-original-title='Remove this question'><i class='fa fa-minus' style='color: red;'></i></a></td>
                </tr>`);

                $('#newQuestion').val('');
            }
            newprotocol.addQuestion = addQuestion;
            function generateAnswersTable(count) {
                var answersTable = '<table id="answersTable_' + count + '" class="table table-striped table-bordered"><thead><tr><th style="max-width:35px;font-size: 14px;" >Weight</th><th style = "font-size: 14px;" >Answer Text</th><th style = "font-size: 16px;" ><a id="addAnswerButton" class="btn btn-success btn-xs" onclick="resgrid.protocols.newprotocol.addAnswer(' + count + ');" data-original-title="Add Answers to Question" ><i class="icon-plus" ></i> Add Answer</a></th></tr></thead><tbody></tbody></table>';
                return answersTable;
            }
            newprotocol.generateAnswersTable = generateAnswersTable;
            function addAnswer(count) {
                var id = generate(4);
                $('#answersTable_' + count + ' tbody').append("<tr><td><input type='number' name='weightForAnswer_" + count + "_" + id + "'' value='weightForAnswer_" + count + "_" + id + "' min='0' max='100' value='0' onkeydown='resgrid.protocols.newprotocol.parseNumber(event)' onkeyup='resgrid.protocols.newprotocol.parseNumber(event)'></td><td><textarea rows='3' cols='30' data-bv-notempty data-bv-notempty-message='Answer is required' id='answerForQuestion_" + count + "_" + id + "' name='answerForQuestion_" + count + "_" + id + "'></textarea></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='tip-top' data-original-title='Remove this answer from the question'><i class='fa fa-minus' style='color: red;'></i></a></td></tr>");
            }
            newprotocol.addAnswer = addAnswer;
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
            newprotocol.generate = generate;


            function parseNumber(e) {
                if (!((e.keyCode > 95 && e.keyCode < 106)
                    || (e.keyCode > 47 && e.keyCode < 58)
                    || e.keyCode === 8)) {
                    e.preventDefault();
                }
            }
            newprotocol.parseNumber = parseNumber;

            function addTrigger() {
                resgrid.protocols.newprotocol.triggerCount++;
                $('#addTriggerModal').modal('hide');
                $('#triggers tbody').first().append(`<tr>
                    <td style='max-width: 215px;'>${$('#newTriggerType').find(":selected").text()}<input type='text' id='triggerType_${newprotocol.triggerCount}' name='triggerType_${newprotocol.triggerCount}' style='display:none;' value='${$('#newTriggerType').val()}'></input></td>
                    <td>${$('#newTriggerStartsOn').val()}<input type='text' id='triggerStartsOn_${newprotocol.triggerCount}' name='triggerStartsOn_${newprotocol.triggerCount}' style='display:none;' value='${$('#newTriggerStartsOn').val()}'></input></td>"
                    <td>${$('#newTriggerEndsOn').val()}<input type='text' id='triggerEndsOn_${newprotocol.triggerCount}' name='triggerEndsOn_${newprotocol.triggerCount}' style='display:none;' value='${$('#newTriggerEndsOn').val()}'></input></td>"
                    <td>${$('#newTriggerCallPriority').find(":selected").text()}<input type='text' id='triggerCallPriority_${newprotocol.triggerCount}' name='triggerCallPriority_${newprotocol.triggerCount}' style='display:none;'value='${$('#newTriggerCallPriority').val()}'></input></td>"
                    <td>${$('#newTriggerCallType').find(":selected").text()}<input type='text' id='triggerCallType_${newprotocol.triggerCount}' name='triggerCallType_${newprotocol.triggerCount}' style='display:none;' value='${$('#newTriggerCallType').val()}'></input></td>"
                    <td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='tip-top' data-original-title='Remove this trigger'><i class='fa fa-minus' style='color: red;'></i></a></td>
                </tr>`);

                $('#newTriggerType').val(0);
                $('#newTriggerStartsOn').val('');
                $('#newTriggerEndsOn').val('');
                $('#newTriggerCallPriority').val(0);
                $('#newTriggerCallType').val(0);
            }
            newprotocol.addTrigger = addTrigger;
        })(newprotocol = protocols.newprotocol || (protocols.newprotocol = {}));
    })(protocols = resgrid.protocols || (resgrid.protocols = {}));
})(resgrid || (resgrid = {}));
