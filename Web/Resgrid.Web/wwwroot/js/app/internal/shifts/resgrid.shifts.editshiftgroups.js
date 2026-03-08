
var resgrid;
(function (resgrid) {
    var shifts;
    (function (shifts) {
        var editshiftgroups;
        (function (editshiftgroups) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Shifts - Edit Groups');
                resgrid.shifts.editshiftgroups.groupsCount = 0;
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Groups/GetAllGroups',
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    resgrid.shifts.editshiftgroups.groupData = data;
                    resgrid.shifts.editshiftgroups.processShiftGroups();
                });
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Personnel/GetRoles',
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    resgrid.shifts.editshiftgroups.roleData = data;
                    resgrid.shifts.editshiftgroups.processShiftGroups();
                });
            });
            function processShiftGroups() {
                if (editshiftgroups.groupData && editshiftgroups.roleData) {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Shifts/GetShiftJson?shiftId=' + $('#Shift_ShiftId').val(),
                        contentType: 'application/json; charset=utf-8',
                        type: 'GET'
                    }).done(function (data) {
                        if (data && data.Groups) {
                            for (var i = 0; i < data.Groups.length; i++) {
                                resgrid.shifts.editshiftgroups.addExistingGroup(data.Groups[i]);
                            }
                        }
                    });
                }
            }
            editshiftgroups.processShiftGroups = processShiftGroups;
            function addExistingGroup(group) {
                resgrid.shifts.editshiftgroups.groupsCount++;
                var i18n = (typeof resgridShiftsI18n !== 'undefined') ? resgridShiftsI18n : {};
                var removeGroupLabel = i18n.removeGroup || 'Remove Group';
                $('#groups tbody').first().append("<tr><td>" + resgrid.shifts.editshiftgroups.generateExistingGroupDropdown(group.DepartmentGroupId, editshiftgroups.groupsCount) + "</td><td>" + resgrid.shifts.editshiftgroups.generateRolesTables(editshiftgroups.groupsCount) + "</td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='btn btn-xs btn-danger' data-original-title='" + removeGroupLabel + "'>" + removeGroupLabel + "</a></td></tr>");
                for (var i = 0; i < group.Roles.length; i++) {
                    addExistingGroupRole(group.Roles[i], resgrid.shifts.editshiftgroups.groupsCount);
                }
            }
            editshiftgroups.addExistingGroup = addExistingGroup;
            function addGroup() {
                resgrid.shifts.editshiftgroups.groupsCount++;
                var i18n = (typeof resgridShiftsI18n !== 'undefined') ? resgridShiftsI18n : {};
                var removeGroupLabel = i18n.removeGroup || 'Remove Group';
                $('#groups tbody').first().append("<tr><td>" + resgrid.shifts.editshiftgroups.generateGroupDropdown(editshiftgroups.groupsCount) + "</td><td>" + resgrid.shifts.editshiftgroups.generateRolesTables(editshiftgroups.groupsCount) + "</td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='btn btn-xs btn-danger' data-original-title='" + removeGroupLabel + "'>" + removeGroupLabel + "</a></td></tr>");
            }
            editshiftgroups.addGroup = addGroup;
            function addGroupRole(count) {
                var timestamp = new Date();
                var i18n = (typeof resgridShiftsI18n !== 'undefined') ? resgridShiftsI18n : {};
                var removeRoleLabel = i18n.removeRole || 'Remove Role';
                var removeRoleTitle = i18n.removeRoleFromGroup || 'Remove this role from the group';
                var roleCountMsg = i18n.roleCountRequired || 'Role count is required';
                $('#groupRolesTable_' + count + ' tbody').append("<tr><td>" + resgrid.shifts.editshiftgroups.generateRoleDropdown(editshiftgroups.groupsCount) + "</td><td><input type='number' min='1' max='999' data-bv-notempty data-bv-notempty-message='" + roleCountMsg + "' id='groupRole_" + count + "_" + timestamp.getUTCMilliseconds() + "' name='groupRole_" + count + "_" + timestamp.getUTCMilliseconds() + "' style='width:75px;' value='1' onkeypress='resgrid.shifts.editshiftgroups.validate(event)'></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='btn btn-xs btn-danger' data-original-title='" + removeRoleTitle + "'>" + removeRoleLabel + "</a></td></tr>");
                addGroupRoleField('groupRole_' + count + '_' + timestamp.getUTCMilliseconds());
            }
            editshiftgroups.addGroupRole = addGroupRole;
            function addExistingGroupRole(role, count) {
                var id = generate(4);
                var i18n = (typeof resgridShiftsI18n !== 'undefined') ? resgridShiftsI18n : {};
                var removeRoleLabel = i18n.removeRole || 'Remove Role';
                var removeRoleTitle = i18n.removeRoleFromGroup || 'Remove this role from the group';
                var roleCountMsg = i18n.roleCountRequired || 'Role count is required';
                $('#groupRolesTable_' + count + ' tbody').append("<tr><td>" + resgrid.shifts.editshiftgroups.generateExistingRoleDropdown(role, editshiftgroups.groupsCount, id) + "</td><td><input type='number' min='1' max='999' data-bv-notempty data-bv-notempty-message='" + roleCountMsg + "' id='groupRole_" + count + "_" + id + "' name='groupRole_" + count + "_" + id + "' style='width:75px;' value='" + role.Required + "' onkeypress='resgrid.shifts.editshiftgroups.validate(event)'></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='btn btn-xs btn-danger' data-original-title='" + removeRoleTitle + "'>" + removeRoleLabel + "</a></td></tr>");
                addGroupRoleField('groupRole_' + count + '_' + id);
            }
            editshiftgroups.addExistingGroupRole = addExistingGroupRole;
            function removeRole() {
                $(this).closest('tr').remove();
            }
            editshiftgroups.removeRole = removeRole;
            function generateGroupDropdown(count) {
                var groupSelect = '<select id="groupSelection_' + count + '" name="groupSelection_' + count + '" class="sl2">';
                for (var i = 0; i < editshiftgroups.groupData.length; i++) {
                    groupSelect += '<option value="' + editshiftgroups.groupData[i].GroupId + '">' + editshiftgroups.groupData[i].Name + '</option>';
                }
                return groupSelect;
            }
            editshiftgroups.generateGroupDropdown = generateGroupDropdown;
            function generateExistingGroupDropdown(groupId, count) {
                var groupSelect = '<select id="groupSelection_' + count + '" name="groupSelection_' + count + '" class="sl2">';
                for (var i = 0; i < resgrid.shifts.editshiftgroups.groupData.length; i++) {
                    if (resgrid.shifts.editshiftgroups.groupData[i].GroupId == groupId) {
                        groupSelect += '<option value="' + editshiftgroups.groupData[i].GroupId + '" selected>' + editshiftgroups.groupData[i].Name + '</option>';
                    }
                    else {
                        groupSelect += '<option value="' + editshiftgroups.groupData[i].GroupId + '">' + editshiftgroups.groupData[i].Name + '</option>';
                    }
                }
                groupSelect += '</select>'
                return groupSelect;
            }
            editshiftgroups.generateExistingGroupDropdown = generateExistingGroupDropdown;
            function generateRoleDropdown(count) {
                var timestamp = new Date();
                var groupSelect = '<select id="roleSelection_' + count + '_' + timestamp.getUTCMilliseconds() + '" name="roleSelection_' + count + '_' + timestamp.getUTCMilliseconds() + '" class="sl2">';
                for (var i = 0; i < editshiftgroups.roleData.length; i++) {
                    groupSelect += '<option value="' + editshiftgroups.roleData[i].RoleId + '">' + editshiftgroups.roleData[i].Name + '</option>';
                }
                return groupSelect;
            }
            editshiftgroups.generateRoleDropdown = generateRoleDropdown;
            function generateExistingRoleDropdown(role, count, id) {
                var groupSelect = '<select id="roleSelection_' + count + '_' + id + '" name="roleSelection_' + count + '_' + id + '" class="sl2">';
                for (var i = 0; i < editshiftgroups.roleData.length; i++) {
                    if (role.PersonnelRoleId === editshiftgroups.roleData[i].RoleId) {
                        groupSelect += '<option value="' + editshiftgroups.roleData[i].RoleId + '" selected>' + editshiftgroups.roleData[i].Name + '</option>';
                    }
                    else {
                        groupSelect += '<option value="' + editshiftgroups.roleData[i].RoleId + '">' + editshiftgroups.roleData[i].Name + '</option>';
                    }
                }
                return groupSelect;
            }
            editshiftgroups.generateExistingRoleDropdown = generateExistingRoleDropdown;
            function generateRolesTables(count) {
                var i18n = (typeof resgridShiftsI18n !== 'undefined') ? resgridShiftsI18n : {};
                var shiftRole = i18n.shiftRoleColumn || 'Shift Role';
                var rolesCount = i18n.rolesCountColumn || 'Roles Count';
                var addRoleLabel = i18n.addRoleToGroup || 'Add Role to Group';
                var addShiftRolesToGroupTitle = i18n.addShiftRolesToGroup || 'Add Shift Roles to Group';
                var rolesTable = '<table id="groupRolesTable_' + count + '" class="table table-striped table-bordered"><thead><tr><th style="font-size: 14px;">' + shiftRole + '</th><th style="font-size: 14px;">' + rolesCount + '</th><th style="font-size: 16px;"><a id="addGroupButton" class="btn btn-success btn-xs" onclick="resgrid.shifts.editshiftgroups.addGroupRole(' + count + ');" data-original-title="' + addShiftRolesToGroupTitle + '"><i class="icon-plus"></i> ' + addRoleLabel + '</a></th></tr></thead><tbody></tbody></table>';
                return rolesTable;
            }
            editshiftgroups.generateRolesTables = generateRolesTables;
            function validate(evt) {
                var theEvent = evt || window.event;
                var key = theEvent.keyCode || theEvent.which;
                key = String.fromCharCode(key);
                var regex = /[0-9]|\./;
                if (!regex.test(key)) {
                    theEvent.returnValue = false;
                    if (theEvent.preventDefault)
                        theEvent.preventDefault();
                }
            }
            editshiftgroups.validate = validate;
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
            editshiftgroups.generate = generate;
        })(editshiftgroups = shifts.editshiftgroups || (shifts.editshiftgroups = {}));
    })(shifts = resgrid.shifts || (resgrid.shifts = {}));
})(resgrid || (resgrid = {}));
