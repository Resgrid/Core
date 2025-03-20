
var resgrid;
(function (resgrid) {
    var notifications;
    (function (notifications) {
        var addNotification;
        (function (addNotification) {
            var userStatusData = {
                '-1': 'Any',
                '0': 'Standing By',
                '1': 'Not Responding',
                '2': 'Responding',
                '3': 'On Scene',
                '4': 'Available Station',
                '5': 'Responding Station',
                '6': 'Responding Scene'
            };
            var userStaffingData = {
                '-1': 'Any',
                '0': 'Available',
                '1': 'Delayed',
                '2': 'Unavailable',
                '3': 'Committed',
                '4': 'On Shift'
            };
            var unitStatusData = {
                '-1': 'Any',
                '0': 'Available',
                '1': 'Delayed',
                '2': 'Unavailable',
                '3': 'Committed',
                '4': 'Out Of Service',
                '5': 'Responding',
                '6': 'On Scene',
                '7': 'Staging',
                '8': 'Returning',
                '9': 'Cancelled',
                '10': 'Released',
                '11': 'Manual',
                '12': 'Enroute'
            };
            $(document).ready(function () {
                $('.sl2').select2();
                $('#Type').on("change", function (e) { switchType($('#Type').val()); });
                $('#SelectedUnitType').on("change", function (e) { getUnitStates($('#SelectedUnitType').val()); });
                setUnitStateDataDropdowns();
                $("#groups").kendoMultiSelect({
                    placeholder: "Select groups...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=1'
                        }
                    }
                });
                $("#roles").kendoMultiSelect({
                    placeholder: "Select roles...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=2'
                        }
                    }
                });
                $("#users").kendoMultiSelect({
                    placeholder: "Select users...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterSelf=true'
                        }
                    }
                });
                $('#Notification_Everyone').change(function () {
                    if (this.checked) {
                        $('#Notification_DepartmentAdmins').prop('checked', false);
                        $('#Notification_LockToGroup').prop('checked', false);
                        var usersMulti = $("#users").data("kendoMultiSelect");
                        usersMulti.value("");
                        usersMulti.enable(false);
                        var rolesMulti = $("#roles").data("kendoMultiSelect");
                        rolesMulti.value("");
                        rolesMulti.enable(false);
                        var groupsMulti = $("#groups").data("kendoMultiSelect");
                        groupsMulti.value("");
                        groupsMulti.enable(false);
                    }
                    else {
                        var usersMulti = $("#users").data("kendoMultiSelect");
                        usersMulti.enable(true);
                        var rolesMulti = $("#roles").data("kendoMultiSelect");
                        rolesMulti.enable(true);
                        var groupsMulti = $("#groups").data("kendoMultiSelect");
                        groupsMulti.enable(true);
                    }
                });
                $('#Notification_DepartmentAdmins').change(function () {
                    if (this.checked) {
                        $('#Notification_Everyone').prop('checked', false);
                        $('#Notification_LockToGroup').prop('checked', false);
                        var usersMulti = $("#users").data("kendoMultiSelect");
                        usersMulti.value("");
                        usersMulti.enable(false);
                        var rolesMulti = $("#roles").data("kendoMultiSelect");
                        rolesMulti.value("");
                        rolesMulti.enable(false);
                        var groupsMulti = $("#groups").data("kendoMultiSelect");
                        groupsMulti.value("");
                        groupsMulti.enable(false);
                    }
                    else {
                        var usersMulti = $("#users").data("kendoMultiSelect");
                        usersMulti.enable(true);
                        var rolesMulti = $("#roles").data("kendoMultiSelect");
                        rolesMulti.enable(true);
                        var groupsMulti = $("#groups").data("kendoMultiSelect");
                        groupsMulti.enable(true);
                    }
                });
                $('#Notification_LockToGroup').change(function () {
                    if (this.checked) {
                        $('#Notification_Everyone').prop('checked', false);
                        $('#Notification_DepartmentAdmins').prop('checked', false);
                        var usersMulti = $("#users").data("kendoMultiSelect");
                        usersMulti.value("");
                        usersMulti.enable(false);
                        var rolesMulti = $("#roles").data("kendoMultiSelect");
                        rolesMulti.enable(true);
                        var groupsMulti = $("#groups").data("kendoMultiSelect");
                        groupsMulti.value("");
                        groupsMulti.enable(false);
                    }
                    else {
                        var usersMulti = $("#users").data("kendoMultiSelect");
                        usersMulti.enable(true);
                        var rolesMulti = $("#roles").data("kendoMultiSelect");
                        rolesMulti.enable(true);
                        var groupsMulti = $("#groups").data("kendoMultiSelect");
                        groupsMulti.enable(true);
                    }
                });
            });
            function getUnitStates(value) {
                if (value) {
                    $('#currentStateMultiControl').empty().append("<select id='currentStates' name='currentStates'></select>");
                    $("#currentStates").kendoMultiSelect({
                        placeholder: "Select available unit states...",
                        dataTextField: "Name",
                        dataValueField: "Id",
                        autoBind: false,
                        dataSource: {
                            type: "json",
                            transport: {
                                read: resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetUnitStatusesLevelsForDepartment?includeAny=False&unitTypeId=' + value
                            }
                        }
                    });
                }
            }
            function setUnitStateDataDropdowns() {
                $('#beforeStateControl').empty().append('<input id="Notification_BeforeData" name="Notification.BeforeData" />');
                $('#currentStateControl').empty().append('<input id="Notification_CurrentData" name="Notification.CurrentData" />');
                $("#Notification_BeforeData").kendoDropDownList({
                    dataTextField: "Name",
                    dataValueField: "Id",
                    index: 0,
                    autoBind: true,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetUnitStatusesLevelsForDepartmentCombined?includeAny=True'
                        }
                    }
                });
                $("#Notification_CurrentData").kendoDropDownList({
                    dataTextField: "Name",
                    dataValueField: "Id",
                    index: 0,
                    autoBind: true,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetUnitStatusesLevelsForDepartmentCombined?includeAny=True'
                        }
                    }
                });
            }
            function setPersonnelStaffingDataDropdowns() {
                $('#beforeStateControl').empty().append('<input id="Notification_BeforeData" name="Notification.BeforeData" />');
                $('#currentStateControl').empty().append('<input id="Notification_CurrentData" name="Notification.CurrentData" />');
                $("#Notification_BeforeData").kendoDropDownList({
                    dataTextField: "Name",
                    dataValueField: "Id",
                    index: 0,
                    autoBind: true,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetPersonnelStaffingLevelsForDepartment?includeAny=True'
                        }
                    }
                });
                $("#Notification_CurrentData").kendoDropDownList({
                    dataTextField: "Name",
                    dataValueField: "Id",
                    index: 0,
                    autoBind: true,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetPersonnelStaffingLevelsForDepartment?includeAny=True'
                        }
                    }
                });
            }
            function setPersonnelStatusDataDropdowns() {
                $('#beforeStateControl').empty().append('<input id="Notification_BeforeData" name="Notification.BeforeData" />');
                $('#currentStateControl').empty().append('<input id="Notification_CurrentData" name="Notification.CurrentData" />');
                $("#Notification_BeforeData").kendoDropDownList({
                    dataTextField: "Name",
                    dataValueField: "Id",
                    index: 0,
                    autoBind: true,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetPersonnelStatusesForDepartment?includeAny=True'
                        }
                    }
                });
                $("#Notification_CurrentData").kendoDropDownList({
                    dataTextField: "Name",
                    dataValueField: "Id",
                    index: 0,
                    autoBind: true,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetPersonnelStatusesForDepartment?includeAny=True'
                        }
                    }
                });
            }
            function switchType(value) {
                if (value) {
                    $('#usersToNotify').show();
                    $('#calendarNotice').hide();
                    $('#shiftNotice').hide();
                    if (value === "0") {
                        $('#beforeState').show();
                        $('#currentState').show();
                        $('#lockToGroupSection').show();
                        $('#lowerLimitGroup').hide();
                        $('#currentStateMulti').hide();
                        $('#roleToMonitor').hide();
                        $('#unitTypesToMonitor').hide();
                        setUnitStateDataDropdowns();
                    }
                    else if (value === "1") {
                        $('#beforeState').show();
                        $('#currentState').show();
                        $('#lockToGroupSection').show();
                        $('#lowerLimitGroup').hide();
                        $('#currentStateMulti').hide();
                        $('#roleToMonitor').hide();
                        $('#unitTypesToMonitor').hide();
                        setPersonnelStaffingDataDropdowns();
                    }
                    else if (value === "2") {
                        $('#beforeState').show();
                        $('#currentState').show();
                        $('#lockToGroupSection').show();
                        $('#lowerLimitGroup').hide();
                        $('#currentStateMulti').hide();
                        $('#roleToMonitor').hide();
                        $('#unitTypesToMonitor').hide();
                        setPersonnelStatusDataDropdowns();
                    }
                    else if (value === "11") {
                        $('#beforeState').hide();
                        $('#currentState').hide();
                        $('#lockToGroupSection').show();
                        $('#lowerLimitGroup').show();
                        $('#roleToMonitor').show();
                        $('#unitTypesToMonitor').hide();
                        $('#currentStateMulti').show();
                        $('#currentStateMultiLabel').empty().append("Available Staffing Levels");
                        $('#currentStateMultiControl').empty().append("<select id='currentStates' name='currentStates'></select>");
                        $("#currentStates").kendoMultiSelect({
                            placeholder: "Select available staffing levels...",
                            dataTextField: "Name",
                            dataValueField: "Id",
                            autoBind: false,
                            dataSource: {
                                type: "json",
                                transport: {
                                    read: resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetPersonnelStaffingLevelsForDepartment?includeAny=False'
                                }
                            }
                        });
                        $('#lowerLimitGroupControl').empty().append("<input id='lowerLimit' value='0' />");
                        $("#lowerLimit").kendoNumericTextBox({
                            format: "0",
                            min: 0,
                            max: 999,
                            step: 1
                        });
                    }
                    else if (value === "12") {
                        $('#beforeState').hide();
                        $('#currentState').hide();
                        $('#lockToGroupSection').show();
                        $('#lowerLimitGroup').show();
                        $('#roleToMonitor').hide();
                        $('#unitTypesToMonitor').show();
                        $('#currentStateMulti').show();
                        $('#currentStateMultiLabel').empty().append("Available Unit States");
                        $('#currentStateMultiControl').empty().append("<select id='currentStates' name='currentStates'></select>");
                        $("#currentStates").kendoMultiSelect({
                            placeholder: "Select available unit states...",
                            dataTextField: "Name",
                            dataValueField: "Id",
                            autoBind: false,
                            dataSource: {
                                type: "json",
                                transport: {
                                    read: resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetUnitStatusesLevelsForDepartment?includeAny=False&unitTypeId=' + $('#SelectedUnitType').val()
                                }
                            }
                        });
                        $('#lowerLimitGroupControl').empty().append("<input id='lowerLimit' value='0' />");
                        $("#lowerLimit").kendoNumericTextBox({
                            format: "0",
                            min: 0,
                            max: 999,
                            step: 1
                        });
                    }
                    else if (value === "13") {
                        $('#beforeState').hide();
                        $('#currentState').hide();
                        $('#lockToGroupSection').hide();
                        $('#lowerLimitGroup').show();
                        $('#roleToMonitor').show();
                        $('#unitTypesToMonitor').hide();
                        $('#currentStateMulti').show();
                        $('#currentStateMultiControl').empty().append("<select id='currentStates' name='currentStates'></select>");
                        $("#currentStates").kendoMultiSelect({
                            placeholder: "Select available states...",
                            dataTextField: "Name",
                            dataValueField: "Id",
                            autoBind: false,
                            dataSource: {
                                type: "json",
                                transport: {
                                    read: resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetPersonnelStaffingLevelsForDepartment?includeAny=False'
                                }
                            }
                        });
                        $('#lowerLimitGroupControl').empty().append("<input id='lowerLimit' value='0' />");
                        $("#lowerLimit").kendoNumericTextBox({
                            format: "0",
                            min: 0,
                            max: 999,
                            step: 1
                        });
                        $('#currentStateMultiLabel').empty().append("Available Staffing Levels");
                    }
                    else if (value === "14") {
                        $('#beforeState').hide();
                        $('#currentState').hide();
                        $('#lockToGroupSection').hide();
                        $('#lowerLimitGroup').show();
                        $('#roleToMonitor').hide();
                        $('#unitTypesToMonitor').show();
                        $('#currentStateMulti').show();
                        $('#currentStateMultiLabel').empty().append("Available Unit States");
                        $('#currentStateMultiControl').empty().append("<select id='currentStates' name='currentStates'></select>");
                        $("#currentStates").kendoMultiSelect({
                            placeholder: "Select available unit states...",
                            dataTextField: "Name",
                            dataValueField: "Id",
                            autoBind: false,
                            dataSource: {
                                type: "json",
                                transport: {
                                    read: resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetUnitStatusesLevelsForDepartment?includeAny=False&unitTypeId=' + $('#SelectedUnitType').val()
                                }
                            }
                        });
                        $('#lowerLimitGroupControl').empty().append("<input id='lowerLimit' value='0' />");
                        $("#lowerLimit").kendoNumericTextBox({
                            format: "0",
                            min: 0,
                            max: 999,
                            step: 1
                        });
                    }
                    else if (value === "15" || value === "5" || value === "16") {
                        $('#beforeState').hide();
                        $('#currentState').hide();
                        $('#lockToGroupSection').show();
                        $('#unitTypesToMonitor').hide();
                        $('#lowerLimitGroup').hide();
                        $('#roleToMonitor').hide();
                        $('#currentStateMulti').hide();
                        $('#usersToNotify').hide();
                        $('#calendarNotice').show();
                    }
                    else if (value === "16" || value === "17" || value === "18") {
                        $('#beforeState').hide();
                        $('#currentState').hide();
                        $('#lockToGroupSection').show();
                        $('#unitTypesToMonitor').hide();
                        $('#lowerLimitGroup').hide();
                        $('#roleToMonitor').hide();
                        $('#currentStateMulti').hide();
                        $('#usersToNotify').hide();
                        $('#shiftNotice').show();
                    }
                    else {
                        $('#beforeState').hide();
                        $('#currentState').hide();
                        $('#lockToGroupSection').show();
                        $('#unitTypesToMonitor').hide();
                        $('#lowerLimitGroup').hide();
                        $('#roleToMonitor').hide();
                        $('#currentStateMulti').hide();
                    }
                }
            }
            function checkform() {
                var lockedToGroup = $('#Notification_LockToGroup').is(":checked");
                var groupAdminsOnly = $('#Notification_SelectedGroupsAdminsOnly').is(":checked");
                var rolesMulti = $("#roles").data("kendoMultiSelect");
                var rolesSelected = rolesMulti.value();
                if (lockedToGroup && rolesSelected.length === 0 && !groupAdminsOnly) {
                    $('#errorInfo').empty().html('<span class="alert alert-danger">If you want to lock the source group you need to select the roles you want to notify in the group or check "Group Admins Only".</span>');
                    return false;
                }
                if ($('#Type').val() === "11" || $('#Type').val() === "12" || $('#Type').val() === "13" || $('#Type').val() === "14") {
                    if (!$('#currentStates').val()) {
                        $('#errorInfo').empty().html('<span class="alert alert-danger">For Availability alerts you need to supply which states are considered available (i.e. Available and Delayed) choose them from the Available States/Staffing multiselect.</span>');
                        return false;
                    }
                }
                return true;
            }
            addNotification.checkform = checkform;
        })(addNotification = notifications.addNotification || (notifications.addNotification = {}));
    })(notifications = resgrid.notifications || (resgrid.notifications = {}));
})(resgrid || (resgrid = {}));
