var resgrid;
(function (resgrid) {
    var commands;
    (function (commands) {
        var newcommand;
        (function (newcommand) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Command - New Definition');
                $('#SelectedType').select2();

                function initSelect2(selector, placeholder, url) {
                    $(selector).select2({
                        placeholder: placeholder,
                        allowClear: true,
                        ajax: {
                            url: url,
                            dataType: 'json',
                            processResults: function (data) {
                                return { results: $.map(data, function (item) { return { id: item.Id, text: item.Name }; }) };
                            }
                        }
                    });
                }

                initSelect2('#unitTypes', 'Select Unit Types...', resgrid.absoluteBaseUrl + '/User/Units/GetUnitTypes');
                initSelect2('#personnelRoles', 'Select Personnel Roles...', resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=2');
                initSelect2('#certifications', 'Select Certifications...', resgrid.absoluteBaseUrl + '/User/Personnel/GetCertifications');

                $('#newAssignmentModal').on('show.bs.modal', function () {
                    $('#assignment-name').val('');
                    $('#description-text').val('');
                    $('#forceRequirements').prop('checked', false);
                    $('#unitTypes, #personnelRoles, #certifications').val(null).trigger('change');
                });
            });

            function addAssignment() {
                $('#newAssignmentModal').modal('hide');
                var unitTypeValues = $('#unitTypes').val() || [];
                var roleValues = $('#personnelRoles').val() || [];
                var certValues = $('#certifications').val() || [];
                $('#assignments tbody').first().append(
                    "<tr><td style='max-width: 215px;'>" + $('#assignment-name').val() +
                    "<input type='text' id='assignmentName_" + newcommand.assignmentCount + "' name='assignmentName_" + newcommand.assignmentCount + "' style='display:none;' disabled='disabled' value='" + $('#assignment-name').val() + "'></input></td>" +
                    "<td>" + $('#description-text').val() +
                    "<input type='text' id='assignmentDescription_" + newcommand.assignmentCount + "' name='assignmentDescription_" + newcommand.assignmentCount + "' style='display:none;' disabled='disabled' value='" + $('#description-text').val() + "'></input>" +
                    "<input type='text' style='display:none;' id='assignmentUnits_" + newcommand.assignmentCount + "' name='assignmentUnits_" + newcommand.assignmentCount + "' disabled='disabled' value='" + unitTypeValues.join(',') + "'></input>" +
                    "<input type='text' style='display:none;' id='assignmentRoles_" + newcommand.assignmentCount + "' name='assignmentRoles_" + newcommand.assignmentCount + "' disabled='disabled' value='" + roleValues.join(',') + "'></input>" +
                    "<input style='display:none;' id='assignmentCerts_" + newcommand.assignmentCount + "' name='assignmentCerts_" + newcommand.assignmentCount + "' disabled='disabled' value='" + certValues.join(',') + "'></input>" +
                    "<input style='display:none;' type='text' id='assignmentLock_" + newcommand.assignmentCount + "' name='assignmentLock_" + newcommand.assignmentCount + "' value='" + $('#forceRequirements').val() + "'></input>" +
                    "</td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='tip-top' data-original-title='Remove this question'><i class='fa fa-minus' style='color: red;'></i></a></td></tr>"
                );
            }
            newcommand.addAssignment = addAssignment;
        })(newcommand = commands.newcommand || (commands.newcommand = {}));
    })(commands = resgrid.commands || (resgrid.commands = {}));
})(resgrid || (resgrid = {}));
