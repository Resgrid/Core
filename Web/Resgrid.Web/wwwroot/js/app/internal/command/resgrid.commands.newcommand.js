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
                $('.assignment-unittypes-inline').select2({ placeholder: 'Any unit type', allowClear: true, width: '100%' });
                $('.assignment-personnelroles-inline').select2({ placeholder: 'Any personnel', allowClear: true, width: '100%' });

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
                const forceRequirements = $('#forceRequirements').is(':checked') ? 'true' : 'false';

                const unitTypeIds = $('#assignment-unittypes').val() || [];
                const roleIds = $('#assignment-personnelroles').val() || [];

                var row = $('<tr></tr>');

                var nameCell = $('<td style="max-width: 215px;"></td>');
                nameCell.append($('<input type="hidden">').attr('name', 'assignmentId_' + index).val('0'));
                nameCell.append($('<input type="text" class="form-control" aria-label="Lane name">').attr('name', 'assignmentName_' + index).val(name));

                var laneCell = $('<td></td>');
                var laneSelect = $('#assignment-lanetype').clone().removeAttr('id').attr({
                    name: 'assignmentLaneType_' + index,
                    'aria-label': 'Lane type'
                }).val(laneType);
                laneCell.append(laneSelect);

                var descriptionCell = $('<td></td>');
                descriptionCell.append($('<textarea class="form-control" rows="2" aria-label="Lane description"></textarea>')
                    .attr('name', 'assignmentDescription_' + index).val(description));

                const requirementsCell = $('<td></td>');
                var unitTypeSelect = $('<select class="form-control assignment-unittypes-inline" multiple="multiple"></select>').attr({
                    name: 'assignmentUnitTypes_' + index,
                    'aria-label': 'Required unit types'
                }).append($('#assignment-unittypes option').clone().removeAttr('data-select2-id')).val(unitTypeIds);
                var roleSelect = $('<select class="form-control assignment-personnelroles-inline" multiple="multiple"></select>').attr({
                    name: 'assignmentRoles_' + index,
                    'aria-label': 'Required personnel roles'
                }).append($('#assignment-personnelroles option').clone().removeAttr('data-select2-id')).css('margin-top', '5px').val(roleIds);
                var enforceContainer = $('<div style="margin-top:5px;"></div>');
                enforceContainer.append($('<input type="hidden">').attr('name', 'assignmentLock_' + index).val('false'));
                var enforceLabel = $('<label style="font-weight:normal; margin:0;"></label>');
                enforceLabel.append($('<input type="checkbox" value="true">').attr('name', 'assignmentLock_' + index).prop('checked', forceRequirements === 'true'));
                enforceLabel.append(document.createTextNode(' Enforce requirements'));
                enforceContainer.append(enforceLabel);
                requirementsCell.append(unitTypeSelect).append(roleSelect).append(enforceContainer);

                var actionCell = $('<td style="text-align:center;"></td>');
                actionCell.append($('<a class="tip-top" data-original-title="Remove this lane"><i class="fa fa-minus" style="color: red;"></i></a>').on('click', function () {
                    row.remove();
                }));

                row.append(nameCell).append(laneCell).append(descriptionCell).append(requirementsCell).append(actionCell);
                $('#assignments tbody').first().append(row);
                unitTypeSelect.select2({ placeholder: 'Any unit type', allowClear: true, width: '100%' });
                roleSelect.select2({ placeholder: 'Any personnel', allowClear: true, width: '100%' });
            }
            newcommand.addAssignment = addAssignment;
        })(newcommand = commands.newcommand || (commands.newcommand = {}));
    })(commands = resgrid.commands || (resgrid.commands = {}));
})(resgrid || (resgrid = {}));
