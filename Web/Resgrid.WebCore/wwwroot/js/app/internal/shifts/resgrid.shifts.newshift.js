
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
                $("#shiftPersonnel").kendoMultiSelect({
                    placeholder: "Select Non-Group Personnel...",
                    dataTextField: "Name",
                    dataValueField: "UserId",
                    autoBind: false,
                    dataSource: {
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter'
                        }
                    }
                });
                $('.groupPersonnelSelect').each(function (i, obj) {
                    $(this).kendoMultiSelect({
                        placeholder: "Select Personnel for Group...",
                        dataTextField: "Name",
                        dataValueField: "UserId",
                        autoBind: false,
                        dataSource: {
                            transport: {
                                read: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter'
                            }
                        }
                    });
                });
                resgrid.shifts.newshift.groupsCount = 0;
                $("#Shift_StartTime").kendoTimePicker({
                    interval: 15
                });
                $("#Shift_EndTime").kendoTimePicker({
                    interval: 15
                });
            });
            function addGroup() {
                resgrid.shifts.newshift.groupsCount++;
                $('#groups tbody').first().append("<tr><td>" + resgrid.shifts.newshift.generateGroupDropdown(newshift.groupsCount) + "</td><td>" + resgrid.shifts.newshift.generateRolesTables(newshift.groupsCount) + "</td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='btn btn-xs btn-danger' data-original-title='Remove this group'>Remove Group</a></td></tr>");
            }
            newshift.addGroup = addGroup;
            function addGroupRole(count) {
                var timestamp = new Date();
                $('#groupRolesTable_' + count + ' tbody').append("<tr><td>" + resgrid.shifts.newshift.generateRoleDropdown(newshift.groupsCount) + "</td><td><input type='number' min='1' max='999' data-bv-notempty data-bv-notempty-message='Role count is required' id='groupRole_" + count + "_" + timestamp.getUTCMilliseconds() + "' name='groupRole_" + count + "_" + timestamp.getUTCMilliseconds() + "' style='width:75px;' value='1'  onkeypress='resgrid.shifts.newshift.validate(event)'></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='btn btn-xs btn-danger' data-original-title='Remove this role from the group'>Remove Role Slot</a></td></tr>");
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
                var rolesTable = '<table id="groupRolesTable_' + count + '" class="table table-striped table-bordered"><thead><tr><th style = "font-size: 14px;" > Shift Role</th><th style = "font-size: 14px;" > Roles Count</th><th style = "font-size: 16px;" ><a id="addGroupButton" class="btn btn-success btn-xs" onclick="resgrid.shifts.newshift.addGroupRole(' + count + ');" data-original-title="Add Shift Roles to Group" ><i class="icon-plus" ></i> Add Role to Group</a></th></tr></thead><tbody></tbody></table>';
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
