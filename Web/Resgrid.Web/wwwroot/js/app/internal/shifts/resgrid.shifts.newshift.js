
var resgrid;
(function (resgrid) {
    var shifts;
    (function (shifts) {
        var newshift;
        (function (newshift) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Shifts - New');
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Groups/GetAllGroups',
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    resgrid.shifts.newshift.groupData = data;
                });
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Personnel/GetRoles',
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    resgrid.shifts.newshift.roleData = data;
                });
                var i18n = (typeof resgridShiftsI18n !== 'undefined') ? resgridShiftsI18n : {};
                $("#shiftPersonnel").select2({
                    placeholder: i18n.selectNonGroupPersonnel || "Select Non-Group Personnel...",
                    allowClear: true,
                    multiple: true,
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter',
                        dataType: 'json',
                        processResults: function (data) {
                            return { results: $.map(data, function (u) { return { id: u.UserId, text: u.Name }; }) };
                        }
                    }
                });
                $('.groupPersonnelSelect').each(function () {
                    $(this).select2({
                        placeholder: i18n.selectPersonnelForGroup || "Select Personnel for Group...",
                        allowClear: true,
                        multiple: true,
                        ajax: {
                            url: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter',
                            dataType: 'json',
                            processResults: function (data) {
                                return { results: $.map(data, function (u) { return { id: u.UserId, text: u.Name }; }) };
                            }
                        }
                    });
                });
                resgrid.shifts.newshift.groupsCount = 0;
                $("#Shift_StartTime").datetimepicker({ datepicker: false, format: 'H:i', step: 15 });
                $("#Shift_EndTime").datetimepicker({ datepicker: false, format: 'H:i', step: 15 });
            });
            function addGroup() {
                resgrid.shifts.newshift.groupsCount++;
                var i18n = (typeof resgridShiftsI18n !== 'undefined') ? resgridShiftsI18n : {};
                var removeGroupLabel = i18n.removeGroup || 'Remove Group';
                $('#groups tbody').first().append("<tr><td>" + resgrid.shifts.newshift.generateGroupDropdown(newshift.groupsCount) + "</td><td>" + resgrid.shifts.newshift.generateRolesTables(newshift.groupsCount) + "</td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='btn btn-xs btn-danger' data-original-title='" + removeGroupLabel + "'>" + removeGroupLabel + "</a></td></tr>");
            }
            newshift.addGroup = addGroup;
            function addGroupRole(count) {
                var timestamp = new Date();
                var i18n = (typeof resgridShiftsI18n !== 'undefined') ? resgridShiftsI18n : {};
                var removeRoleLabel = i18n.removeRole || 'Remove Role';
                var removeRoleTitle = i18n.removeRoleFromGroup || 'Remove this role from the group';
                var roleCountMsg = i18n.roleCountRequired || 'Role count is required';
                $('#groupRolesTable_' + count + ' tbody').append("<tr><td>" + resgrid.shifts.newshift.generateRoleDropdown(newshift.groupsCount) + "</td><td><input type='number' min='1' max='999' data-bv-notempty data-bv-notempty-message='" + roleCountMsg + "' id='groupRole_" + count + "_" + timestamp.getUTCMilliseconds() + "' name='groupRole_" + count + "_" + timestamp.getUTCMilliseconds() + "' style='width:75px;' value='1'  onkeypress='resgrid.shifts.newshift.validate(event)'></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='btn btn-xs btn-danger' data-original-title='" + removeRoleTitle + "'>" + removeRoleLabel + "</a></td></tr>");
                addGroupRoleField('groupRole_' + count + '_' + timestamp.getUTCMilliseconds());
            }
            newshift.addGroupRole = addGroupRole;
            function removeRole() {
                $(this).closest('tr').remove();
            }
            newshift.removeRole = removeRole;
            function generateGroupDropdown(count) {
              if (newshift && newshift.groupData) {
                var groupSelect = '<select id="groupSelection_' +
                  count +
                  '" name="groupSelection_' +
                  count +
                  '" class="sl2">';
                for (var i = 0; i < newshift.groupData.length; i++) {
                  groupSelect += '<option value="' +
                    newshift.groupData[i].GroupId +
                    '">' +
                    newshift.groupData[i].Name +
                    '</option>';
                }
                return groupSelect;
              }
            }
            newshift.generateGroupDropdown = generateGroupDropdown;
            function generateRoleDropdown(count) {
              if (newshift && newshift.roleData) {
                var timestamp = new Date();
                var groupSelect = '<select id="roleSelection_' +
                  count +
                  '_' +
                  timestamp.getUTCMilliseconds() +
                  '" name="roleSelection_' +
                  count +
                  '_' +
                  timestamp.getUTCMilliseconds() +
                  '" class="sl2">';
                for (var i = 0; i < newshift.roleData.length; i++) {
                  groupSelect += '<option value="' +
                    newshift.roleData[i].RoleId +
                    '">' +
                    newshift.roleData[i].Name +
                    '</option>';
                }
                return groupSelect;
              }
            }
            newshift.generateRoleDropdown = generateRoleDropdown;
            function generateRolesTables(count) {
                var i18n = (typeof resgridShiftsI18n !== 'undefined') ? resgridShiftsI18n : {};
                var shiftRole = i18n.shiftRoleColumn || 'Shift Role';
                var rolesCount = i18n.rolesCountColumn || 'Roles Count';
                var addRoleLabel = i18n.addRoleToGroup || 'Add Role to Group';
                var addShiftRolesToGroupTitle = i18n.addShiftRolesToGroup || 'Add Shift Roles to Group';
                var rolesTable = '<table id="groupRolesTable_' + count + '" class="table table-striped table-bordered"><thead><tr><th style="font-size: 14px;">' + shiftRole + '</th><th style="font-size: 14px;">' + rolesCount + '</th><th style="font-size: 16px;"><a id="addGroupButton" class="btn btn-success btn-xs" onclick="resgrid.shifts.newshift.addGroupRole(' + count + ');" data-original-title="' + addShiftRolesToGroupTitle + '"><i class="icon-plus"></i> ' + addRoleLabel + '</a></th></tr></thead><tbody></tbody></table>';
                return rolesTable;
            }
            newshift.generateRolesTables = generateRolesTables;
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
            newshift.validate = validate;
        })(newshift = shifts.newshift || (shifts.newshift = {}));
    })(shifts = resgrid.shifts || (resgrid.shifts = {}));
})(resgrid || (resgrid = {}));
