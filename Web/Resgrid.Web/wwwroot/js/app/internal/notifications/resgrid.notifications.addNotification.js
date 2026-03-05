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

                function initSelect2Multi(selector, placeholder, url, valueField, textField) {
                    valueField = valueField || 'Id';
                    textField = textField || 'Name';
                    $(selector).select2({
                        placeholder: placeholder,
                        allowClear: true,
                        multiple: true,
                        ajax: {
                            url: url,
                            dataType: 'json',
                            processResults: function (data) {
                                return { results: $.map(data, function (item) { return { id: item[valueField], text: item[textField] }; }) };
                            }
                        }
                    });
                }

                initSelect2Multi('#groups', 'Select groups...', resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=1');
                initSelect2Multi('#roles', 'Select roles...', resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=2');
                initSelect2Multi('#users', 'Select users...', resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterSelf=true');

                function toggleRecipients(disable) {
                    ['#users', '#roles', '#groups'].forEach(function (sel) {
                        $(sel).prop('disabled', disable).val(null).trigger('change');
                    });
                }
                function enableRecipients() {
                    ['#users', '#roles', '#groups'].forEach(function (sel) { $(sel).prop('disabled', false); });
                }

                $('#Notification_Everyone').change(function () {
                    if (this.checked) {
                        $('#Notification_DepartmentAdmins, #Notification_LockToGroup').prop('checked', false);
                        toggleRecipients(true);
                    } else { enableRecipients(); }
                });
                $('#Notification_DepartmentAdmins').change(function () {
                    if (this.checked) {
                        $('#Notification_Everyone, #Notification_LockToGroup').prop('checked', false);
                        toggleRecipients(true);
                    } else { enableRecipients(); }
                });
                $('#Notification_LockToGroup').change(function () {
                    if (this.checked) {
                        $('#Notification_Everyone, #Notification_DepartmentAdmins').prop('checked', false);
                        $('#users').prop('disabled', true).val(null).trigger('change');
                        $('#groups').prop('disabled', true).val(null).trigger('change');
                        $('#roles').prop('disabled', false);
                    } else { enableRecipients(); }
                });
            });

            function initCurrentStatesSelect2(url, placeholder) {
                $('#currentStateMultiControl').empty().append("<select id='currentStates' name='currentStates' multiple='multiple' style='width:100%'></select>");
                $('#currentStates').select2({
                    placeholder: placeholder,
                    allowClear: true,
                    multiple: true,
                    ajax: {
                        url: url,
                        dataType: 'json',
                        processResults: function (data) {
                            return { results: $.map(data, function (item) { return { id: item.Id, text: item.Name }; }) };
                        }
                    }
                });
            }

            function initDropDown(selector, url) {
                $.getJSON(url, function (data) {
                    var $sel = $(selector).empty().append('<option value="">-- Any --</option>');
                    $.each(data, function (i, item) { $sel.append('<option value="' + item.Id + '">' + item.Name + '</option>'); });
                });
            }

            function getUnitStates(value) {
                if (value) {
                    initCurrentStatesSelect2(resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetUnitStatusesLevelsForDepartment?includeAny=False&unitTypeId=' + value, 'Select available unit states...');
                }
            }
            function setUnitStateDataDropdowns() {
                $('#beforeStateControl').empty().append('<select id="Notification_BeforeData" name="Notification.BeforeData" style="width:100%"></select>');
                $('#currentStateControl').empty().append('<select id="Notification_CurrentData" name="Notification.CurrentData" style="width:100%"></select>');
                var url = resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetUnitStatusesLevelsForDepartmentCombined?includeAny=True';
                initDropDown('#Notification_BeforeData', url);
                initDropDown('#Notification_CurrentData', url);
            }
            function setPersonnelStaffingDataDropdowns() {
                $('#beforeStateControl').empty().append('<select id="Notification_BeforeData" name="Notification.BeforeData" style="width:100%"></select>');
                $('#currentStateControl').empty().append('<select id="Notification_CurrentData" name="Notification.CurrentData" style="width:100%"></select>');
                var url = resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetPersonnelStaffingLevelsForDepartment?includeAny=True';
                initDropDown('#Notification_BeforeData', url);
                initDropDown('#Notification_CurrentData', url);
            }
            function setPersonnelStatusDataDropdowns() {
                $('#beforeStateControl').empty().append('<select id="Notification_BeforeData" name="Notification.BeforeData" style="width:100%"></select>');
                $('#currentStateControl').empty().append('<select id="Notification_CurrentData" name="Notification.CurrentData" style="width:100%"></select>');
                var url = resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetPersonnelStatusesForDepartment?includeAny=True';
                initDropDown('#Notification_BeforeData', url);
                initDropDown('#Notification_CurrentData', url);
            }

            function initLowerLimit() {
                $('#lowerLimitGroupControl').empty().append("<input id='lowerLimit' name='lowerLimit' type='number' class='form-control' value='0' min='0' max='999' step='1' style='width:100px' />");
            }

            function switchType(value) {
                if (!value) return;
                $('#usersToNotify').show();
                $('#calendarNotice, #shiftNotice').hide();
                if (value === "0") {
                    $('#beforeState, #currentState, #lockToGroupSection').show();
                    $('#lowerLimitGroup, #currentStateMulti, #roleToMonitor, #unitTypesToMonitor').hide();
                    setUnitStateDataDropdowns();
                } else if (value === "1") {
                    $('#beforeState, #currentState, #lockToGroupSection').show();
                    $('#lowerLimitGroup, #currentStateMulti, #roleToMonitor, #unitTypesToMonitor').hide();
                    setPersonnelStaffingDataDropdowns();
                } else if (value === "2") {
                    $('#beforeState, #currentState, #lockToGroupSection').show();
                    $('#lowerLimitGroup, #currentStateMulti, #roleToMonitor, #unitTypesToMonitor').hide();
                    setPersonnelStatusDataDropdowns();
                } else if (value === "11") {
                    $('#beforeState, #currentState').hide();
                    $('#lockToGroupSection, #lowerLimitGroup, #roleToMonitor, #currentStateMulti').show();
                    $('#unitTypesToMonitor').hide();
                    $('#currentStateMultiLabel').empty().append("Available Staffing Levels");
                    initCurrentStatesSelect2(resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetPersonnelStaffingLevelsForDepartment?includeAny=False', 'Select available staffing levels...');
                    initLowerLimit();
                } else if (value === "12") {
                    $('#beforeState, #currentState').hide();
                    $('#lockToGroupSection, #lowerLimitGroup, #unitTypesToMonitor, #currentStateMulti').show();
                    $('#roleToMonitor').hide();
                    $('#currentStateMultiLabel').empty().append("Available Unit States");
                    initCurrentStatesSelect2(resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetUnitStatusesLevelsForDepartment?includeAny=False&unitTypeId=' + $('#SelectedUnitType').val(), 'Select available unit states...');
                    initLowerLimit();
                } else if (value === "13") {
                    $('#beforeState, #currentState').hide();
                    $('#lowerLimitGroup, #roleToMonitor, #currentStateMulti').show();
                    $('#lockToGroupSection, #unitTypesToMonitor').hide();
                    initCurrentStatesSelect2(resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetPersonnelStaffingLevelsForDepartment?includeAny=False', 'Select available states...');
                    initLowerLimit();
                    $('#currentStateMultiLabel').empty().append("Available Staffing Levels");
                } else if (value === "14") {
                    $('#beforeState, #currentState').hide();
                    $('#lowerLimitGroup, #unitTypesToMonitor, #currentStateMulti').show();
                    $('#lockToGroupSection, #roleToMonitor').hide();
                    $('#currentStateMultiLabel').empty().append("Available Unit States");
                    initCurrentStatesSelect2(resgrid.absoluteBaseUrl + '/User/CustomStatuses/GetUnitStatusesLevelsForDepartment?includeAny=False&unitTypeId=' + $('#SelectedUnitType').val(), 'Select available unit states...');
                    initLowerLimit();
                } else if (value === "15" || value === "5" || value === "16") {
                    $('#beforeState, #currentState, #unitTypesToMonitor, #lowerLimitGroup, #roleToMonitor, #currentStateMulti, #usersToNotify').hide();
                    $('#lockToGroupSection, #calendarNotice').show();
                } else if (value === "17" || value === "18") {
                    $('#beforeState, #currentState, #unitTypesToMonitor, #lowerLimitGroup, #roleToMonitor, #currentStateMulti, #usersToNotify').hide();
                    $('#lockToGroupSection, #shiftNotice').show();
                } else {
                    $('#beforeState, #currentState').hide();
                    $('#lockToGroupSection').show();
                    $('#unitTypesToMonitor, #lowerLimitGroup, #roleToMonitor, #currentStateMulti').hide();
                }
            }

            function checkform() {
                var lockedToGroup = $('#Notification_LockToGroup').is(":checked");
                var groupAdminsOnly = $('#Notification_SelectedGroupsAdminsOnly').is(":checked");
                var rolesSelected = $('#roles').val() || [];
                if (lockedToGroup && rolesSelected.length === 0 && !groupAdminsOnly) {
                    $('#errorInfo').empty().html('<span class="alert alert-danger">If you want to lock the source group you need to select the roles you want to notify in the group or check "Group Admins Only".</span>');
                    return false;
                }
                if (['11','12','13','14'].indexOf($('#Type').val()) !== -1) {
                    if (!$('#currentStates').val() || $('#currentStates').val().length === 0) {
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
