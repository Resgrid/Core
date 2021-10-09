
var resgrid;
(function (resgrid) {
    var forms;
    (function (forms) {
        var newform;
        (function (newform) {
            var formBuilder;

            $(document).ready(function () {
                resgrid.common.analytics.track('Forms - New');

                var options = {
                    disableFields: ['autocomplete', 'button', 'file', 'hidden',],
                    disabledActionButtons: ['data', 'save']
                };

                formBuilder = $(document.getElementById('fb-editor')).formBuilder(options);

                $(document).on('click', '#addCallAutomationButton', function () {
                    resgrid.forms.newform.addCallAutomation();

                    return true;
                });

                $('#callAutomations').on('change', '.callAutomationTriggerOptions', function (e) {
                    var forCount = $(e.target).attr("data-callAutomationCount");
                    var type = $(e.target).val();

                    if (type == 1) {
                        $.ajax({
                            url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCallTypes',
                            contentType: 'application/json; charset=utf-8',
                            type: 'GET'
                        }).done(function (result) {
                            if (result) {
                                $(`#callAutomationOperationValue_${forCount} option`).remove();

                                result.forEach(callType => $(`#callAutomationOperationValue_${forCount}`).append(`<option value="${callType.Id}" >${callType.Name}</option>`));
                            }
                        });
                    } else if (type == 2) {
                        $.ajax({
                            url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCallPriorities',
                            contentType: 'application/json; charset=utf-8',
                            type: 'GET'
                        }).done(function (result) {
                            if (result) {
                                $(`#callAutomationOperationValue_${forCount} option`).remove();

                                result.forEach(callType => $(`#callAutomationOperationValue_${forCount}`).append(`<option value="${callType.Id}" >${callType.Name}</option>`));
                            }
                        });
                    } else {
                        $(`#callAutomationOperationValue_${forCount} option`).remove();
                    }
                    e.preventDefault();
                });

                $(document).on('submit', '#newFormForm', function () {
                    var data = formBuilder.actions.getData('json', false);
                    $('#Data').val(data);

                    return true;
                });

                resgrid.forms.newform.callAutomationsCount = 0;
            });
            function addCallAutomation() {
                resgrid.forms.newform.callAutomationsCount++;

                $('#callAutomations tbody').first().append(`<tr>
                    <td>IF</td>
                    <td style='max-width: 215px;'><input type='text' id='callAutomationTriggerField_${newform.callAutomationsCount}' name='callAutomationTriggerField_${newform.callAutomationsCount}'></input></td>
                    <td>EQUALS</td>
                    <td style='max-width: 215px;'><input type='text' id='callAutomationTriggerValue_${newform.callAutomationsCount}' name='callAutomationTriggerValue_${newform.callAutomationsCount}'></input></td>
                    <td>THEN</td>
                    <td><select id='callAutomationOperationType_${newform.callAutomationsCount}' name='callAutomationOperationType_${newform.callAutomationsCount}' class='callAutomationTriggerOptions' data-callAutomationCount='${newform.callAutomationsCount}'><option value="0">Select One</option><option value="1">Set Call Type</option><option value="2">Set Call Priority</option></select></td>
                    <td>TO</td>
                    <td><select id='callAutomationOperationValue_${newform.callAutomationsCount}' name='callAutomationOperationValue_${newform.callAutomationsCount}' class='callAutomationTriggerValues' data-callAutomationCount='${newform.callAutomationsCount}'></select></td>
                    <td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='tip-top' data-original-title='Remove this form automation'><i class='fa fa-minus' style='color: red;'></i></a></td>
                </tr>`);
            }
            newform.addCallAutomation = addCallAutomation;

        })(newform = forms.newform || (forms.newform = {}));
    })(forms = resgrid.forms || (resgrid.forms = {}));
})(resgrid || (resgrid = {}));
