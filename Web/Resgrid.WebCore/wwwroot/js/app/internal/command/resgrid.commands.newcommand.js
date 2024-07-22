var resgrid;
(function (resgrid) {
    var commands;
    (function (commands) {
        var newcommand;
        (function (newcommand) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Command - New Definition');
                //$("#editor").kendoEditor();
                $('#SelectedType').select2();
                $("#unitTypes").kendoMultiSelect({
                    placeholder: "Select Unit Types...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Units/GetUnitTypes'
                        }
                    }
                });
                $("#personnelRoles").kendoMultiSelect({
                    placeholder: "Select Personnel Roles...",
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
                $("#certifications").kendoMultiSelect({
                    placeholder: "Select Certifications...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Personnel/GetCertifications'
                        }
                    }
                });
                $('#newAssignmentModal').on('show.bs.modal', function (event) {
                    $('#assignment-name').val('');
                    $('#description-text').val('');
                    $('#forceRequirements').prop('checked', false);
                    var unitTypesMulti = $("#unitTypes").data("kendoMultiSelect");
                    unitTypesMulti.value("");
                    var rolesTypesMulti = $("#personnelRoles").data("kendoMultiSelect");
                    rolesTypesMulti.value("");
                    var certificationsTypesMulti = $("#certifications").data("kendoMultiSelect");
                    certificationsTypesMulti.value("");
                });
            });
            function addAssignment() {
                $('#newAssignmentModal').modal('hide');
                var unitTypesMulti = $("#unitTypes").data("kendoMultiSelect");
                $('#assignments tbody').first().append("<tr><td style='max-width: 215px;'>" + $('#assignment-name').val() + "<input type='text' id='assignmentName_" + newcommand.assignmentCount + "' name='assignmentName_" + newcommand.assignmentCount + "' style='display:none;' disabled='disabled' value='" + $('#assignment-name').val() + "'></input></td><td>" + $('#description-text').val() + "<input type='text' id='assignmentDescription_" + newcommand.assignmentCount + "' name='assignmentDescription_" + newcommand.assignmentCount + "' style='display:none;' disabled='disabled' value='" + $('#description-text').val() + "'></input>" + unitTypesMulti.value() + "<input type='text' style='display:none;' id='assignmentUnits_" + newcommand.assignmentCount + "' name='assignmentUnits_" + newcommand.assignmentCount + "'  disabled='disabled' value='" + unitTypesMulti.value() + "'></input><input type='text' style='display:none;' id='assignmentRoles_" + newcommand.assignmentCount + "' name='assignmentRoles_" + newcommand.assignmentCount + "'  disabled='disabled'></input><input style='display:none;' id='assignmentCerts_" + newcommand.assignmentCount + "' name='assignmentCerts_" + newcommand.assignmentCount + "' disabled='disabled'></input><input style='display:none;' type='text' id='assignmentLock_" + newcommand.assignmentCount + "' name='assignmentLock_" + newcommand.assignmentCount + "' value='" + $('#forceRequirements').val() + "'></input></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='tip-top' data-original-title='Remove this question'><i class='fa fa-minus' style='color: red;'></i></a></td></tr>");
            }
            newcommand.addAssignment = addAssignment;
        })(newcommand = commands.newcommand || (commands.newcommand = {}));
    })(commands = resgrid.commands || (resgrid.commands = {}));
})(resgrid || (resgrid = {}));
