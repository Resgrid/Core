var resgrid;
(function (resgrid) {
    var commands;
    (function (commands) {
        var newcommand;
        (function (newcommand) {
            newcommand.assignmentCount = 0;

            $(document).ready(function () {
                resgrid.common.analytics.track('Command - Board Definition');
                $('#SelectedType').select2();
                $('#assignment-unittypes').select2({ placeholder: 'Any unit type', allowClear: true });
                $('#assignment-personnelroles').select2({ placeholder: 'Any personnel', allowClear: true });

                // Continue numbering after any server-rendered lane rows (Edit page).
                newcommand.assignmentCount = parseInt($('#assignments').data('next-index'), 10) || 0;

                $('#newAssignmentModal').on('show.bs.modal', function () {
                    $('#assignment-name').val('');
                    $('#description-text').val('');
                    $('#assignment-lanetype').val('0');
                    $('#forceRequirements').prop('checked', false);
                    $('#assignment-unittypes, #assignment-personnelroles').val(null).trigger('change');
                });
            });

            function addAssignment() {
                var name = $.trim($('#assignment-name').val());
                if (!name) {
                    return;
                }

                $('#newAssignmentModal').modal('hide');

                var index = newcommand.assignmentCount++;
                var description = $('#description-text').val();
                var laneType = $('#assignment-lanetype').val() || '0';
                var laneTypeName = $('#assignment-lanetype option:selected').text();
                var forceRequirements = $('#forceRequirements').is(':checked') ? 'true' : 'false';

                var unitTypeIds = $('#assignment-unittypes').val() || [];
                var unitTypeNames = $('#assignment-unittypes option:selected').map(function () { return $(this).text(); }).get();
                var roleIds = $('#assignment-personnelroles').val() || [];
                var roleNames = $('#assignment-personnelroles option:selected').map(function () { return $(this).text(); }).get();

                var row = $('<tr></tr>');

                var nameCell = $('<td style="max-width: 215px;"></td>').text(name);
                nameCell.append($('<input type="hidden">').attr('name', 'assignmentId_' + index).val('0'));
                nameCell.append($('<input type="hidden">').attr('name', 'assignmentName_' + index).val(name));

                var laneCell = $('<td></td>').text(laneTypeName);
                laneCell.append($('<input type="hidden">').attr('name', 'assignmentLaneType_' + index).val(laneType));

                var descriptionCell = $('<td></td>').text(description);
                descriptionCell.append($('<input type="hidden">').attr('name', 'assignmentDescription_' + index).val(description));

                var requirementsCell = $('<td></td>');
                if (unitTypeNames.length > 0) {
                    requirementsCell.append($('<div></div>').append($('<strong></strong>').text('Units: ')).append(document.createTextNode(unitTypeNames.join(', '))));
                }
                if (roleNames.length > 0) {
                    requirementsCell.append($('<div></div>').append($('<strong></strong>').text('Roles: ')).append(document.createTextNode(roleNames.join(', '))));
                }
                if (forceRequirements === 'true') {
                    requirementsCell.append($('<div></div>').append($('<span class="label label-warning">Enforced</span>')));
                }
                requirementsCell.append($('<input type="hidden">').attr('name', 'assignmentUnitTypes_' + index).val(unitTypeIds.join(',')));
                requirementsCell.append($('<input type="hidden">').attr('name', 'assignmentRoles_' + index).val(roleIds.join(',')));
                requirementsCell.append($('<input type="hidden">').attr('name', 'assignmentLock_' + index).val(forceRequirements));

                var actionCell = $('<td style="text-align:center;"></td>');
                actionCell.append($('<a class="tip-top" data-original-title="Remove this lane"><i class="fa fa-minus" style="color: red;"></i></a>').on('click', function () {
                    row.remove();
                }));

                row.append(nameCell).append(laneCell).append(descriptionCell).append(requirementsCell).append(actionCell);
                $('#assignments tbody').first().append(row);
            }
            newcommand.addAssignment = addAssignment;
        })(newcommand = commands.newcommand || (commands.newcommand = {}));
    })(commands = resgrid.commands || (resgrid.commands = {}));
})(resgrid || (resgrid = {}));
