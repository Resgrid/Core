
var resgrid;
(function (resgrid) {
    var security;
    (function (security) {
        var permissions;
        (function (permissions) {
            function initPermRoles(selector, permType) {
                $(selector).select2({
                    placeholder: "Select roles...",
                    allowClear: true,
                    multiple: true,
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Personnel/GetRoles',
                        dataType: 'json',
                        processResults: function (data) {
                            return { results: $.map(data, function (i) { return { id: i.RoleId, text: i.Name }; }) };
                        }
                    }
                });
                $(selector).on('change', function () {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermissionData?type=' + permType + '&data=' + encodeURIComponent(($(selector).val() || []).join(',')),
                        type: 'GET'
                    });
                });
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Security/GetRolesForPermission?type=' + permType,
                    contentType: 'application/json', type: 'GET'
                }).done(function (data) {
                    if (data) {
                        data.split(',').forEach(function (v) {
                            if (v) { $(selector).append(new Option(v, v, true, true)); }
                        });
                        $(selector).trigger('change');
                    }
                });
            }
            $(document).ready(function () {
                resgrid.common.analytics.track('Security Permissions');
                $('#AddUsers').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=0&perm=' + val,
                        type: 'GET'
                    }).done(function (results) {
                    });
                });
                $('#RemoveUsers').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=1&perm=' + val,
                        type: 'GET'
                    }).done(function (results) {
                    });
                });
                $('#CreateCall').change(function () {
                    var val = this.value;
                    if (val === "2") {
                        $('#callCreateNoRolesSpan').hide();
                        $('#callCreateRolesDiv').show();
                    }
                    else {
                        $('#callCreateNoRolesSpan').show();
                        $('#callCreateRolesDiv').hide();
                    }
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=2&perm=' + val,
                        type: 'GET'
                    }).done(function (results) {
                    });
                });
                initPermRoles("#callCreateRoles", 2);
                if ($("#CreateCall").val() === "2") {
                    $('#callCreateNoRolesSpan').hide();
                    $('#callCreateRolesDiv').show();
                }
                else {
                    $('#callCreateNoRolesSpan').show();
                    $('#callCreateRolesDiv').hide();
                }
                $('#CreateTraining').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=3&perm=' + val,
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#CreateTraining").val() === "2") {
                        $('#trainingCreateNoRolesSpan').hide();
                        $('#trainingCreateRolesDiv').show();
                    }
                    else {
                        $('#trainingCreateNoRolesSpan').show();
                        $('#trainingCreateRolesDiv').hide();
                    }
                });
                if ($("#CreateTraining").val() === "2") {
                    $('#trainingCreateNoRolesSpan').hide();
                    $('#trainingCreateRolesDiv').show();
                }
                else {
                    $('#trainingCreateNoRolesSpan').show();
                    $('#trainingCreateRolesDiv').hide();
                }
                initPermRoles("#trainingCreateRoles", 3);
                $('#CreateDocument').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=4&perm=' + val,
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#CreateDocument").val() === "2") {
                        $('#documentCreateNoRolesSpan').hide();
                        $('#documentCreateRolesDiv').show();
                    }
                    else {
                        $('#documentCreateNoRolesSpan').show();
                        $('#documentCreateRolesDiv').hide();
                    }
                });
                if ($("#CreateDocument").val() === "2") {
                    $('#documentCreateNoRolesSpan').hide();
                    $('#documentCreateRolesDiv').show();
                }
                else {
                    $('#documentCreateNoRolesSpan').show();
                    $('#documentCreateRolesDiv').hide();
                }
                initPermRoles("#documentCreateRoles", 4);
                $('#CreateCalendarEntry').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=5&perm=' + val,
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#CreateCalendarEntry").val() === "2") {
                        $('#calendarEntriesCreateNoRolesSpan').hide();
                        $('#calendarEntriesCreateRolesDiv').show();
                    }
                    else {
                        $('#calendarEntriesCreateNoRolesSpan').show();
                        $('#calendarEntriesCreateRolesDiv').hide();
                    }
                });
                if ($("#CreateCalendarEntry").val() === "2") {
                    $('#calendarEntriesCreateNoRolesSpan').hide();
                    $('#calendarEntriesCreateRolesDiv').show();
                }
                else {
                    $('#calendarEntriesCreateNoRolesSpan').show();
                    $('#calendarEntriesCreateRolesDiv').hide();
                }
                initPermRoles("#calendarEntiresCreateRoles", 5);
                $('#CreateNote').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=6&perm=' + val,
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#CreateNote").val() === "2") {
                        $('#noteCreateNoRolesSpan').hide();
                        $('#noteCreateRolesDiv').show();
                    }
                    else {
                        $('#noteCreateNoRolesSpan').show();
                        $('#noteCreateRolesDiv').hide();
                    }
                });
                if ($("#CreateNote").val() === "2") {
                    $('#noteCreateNoRolesSpan').hide();
                    $('#noteCreateRolesDiv').show();
                }
                else {
                    $('#noteCreateNoRolesSpan').show();
                    $('#noteCreateRolesDiv').hide();
                }
                initPermRoles("#noteCreateRoles", 6);
                $('#CreateLog').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=7&perm=' + val,
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#CreateLog").val() === "2") {
                        $('#logCreateNoRolesSpan').hide();
                        $('#logCreateRolesDiv').show();
                    }
                    else {
                        $('#logCreateNoRolesSpan').show();
                        $('#logCreateRolesDiv').hide();
                    }
                });
                if ($("#CreateLog").val() === "2") {
                    $('#logCreateNoRolesSpan').hide();
                    $('#logCreateRolesDiv').show();
                }
                else {
                    $('#logCreateNoRolesSpan').show();
                    $('#logCreateRolesDiv').hide();
                }
                initPermRoles("#logCreateRoles", 7);

                // Delete Log
                ////////////////////////////////////////////////////////
                $('#DeleteLog').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=27&perm=' + val,
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#DeleteLog").val() === "2") {
                        $('#logDeleteNoRolesSpan').hide();
                        $('#logDeleteRolesDiv').show();
                    }
                    else {
                        $('#logDeleteNoRolesSpan').show();
                        $('#logDeleteRolesDiv').hide();
                    }
                });
                if ($("#DeleteLog").val() === "2") {
                    $('#logDeleteNoRolesSpan').hide();
                    $('#logDeleteRolesDiv').show();
                }
                else {
                    $('#logDeleteNoRolesSpan').show();
                    $('#logDeleteRolesDiv').hide();
                }
                initPermRoles("#logDeleteRoles", 27);
                ////////////////////////////////////////////////////////

                $('#CreateShift').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=8&perm=' + val,
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#CreateShift").val() === "2") {
                        $('#shiftCreateNoRolesSpan').hide();
                        $('#shiftCreateRolesDiv').show();
                    }
                    else {
                        $('#shiftCreateNoRolesSpan').show();
                        $('#shiftCreateRolesDiv').hide();
                    }
                });
                if ($("#CreateShift").val() === "2") {
                    $('#shiftCreateNoRolesSpan').hide();
                    $('#shiftCreateRolesDiv').show();
                }
                else {
                    $('#shiftCreateNoRolesSpan').show();
                    $('#shiftCreateRolesDiv').hide();
                }
                initPermRoles("#shiftCreateRoles", 8);
                $('#ViewPersonalInfo').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=9&perm=' + val,
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#ViewPersonalInfo").val() === "2") {
                        $('#personalInfooRolesSpan').hide();
                        $('#personalInfoRolesDiv').show();
                    }
                    else {
                        $('#personalInfooRolesSpan').show();
                        $('#personalInfoRolesDiv').hide();
                    }
                });
                if ($("#ViewPersonalInfo").val() === "2") {
                    $('#personalInfooRolesSpan').hide();
                    $('#personalInfoRolesDiv').show();
                }
                else {
                    $('#personalInfooRolesSpan').show();
                    $('#personalInfoRolesDiv').hide();
                }
                initPermRoles("#personalInfoRoles", 9);
                $('#AdjustInventory').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=10&perm=' + val,
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#AdjustInventory").val() === "2") {
                        $('#adjustInventoryRolesSpan').hide();
                        $('#adjustInventoryRolesDiv').show();
                    }
                    else {
                        $('#adjustInventoryRolesSpan').show();
                        $('#adjustInventoryRolesDiv').hide();
                    }
                });
                if ($("#AdjustInventory").val() === "2") {
                    $('#adjustInventoryRolesSpan').hide();
                    $('#adjustInventoryRolesDiv').show();
                }
                else {
                    $('#adjustInventoryRolesSpan').show();
                    $('#adjustInventoryRolesDiv').hide();
                }
                initPermRoles("#adjustInventoryRoles", 10);
                $('#ViewPersonnelLocation').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=11&perm=' + val + '&lockToGroup=' + $('#LockViewPersonneLocationToGroup').is(':checked'),
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#ViewPersonnelLocation").val() === "2") {
                        $('#viewPersonnelLocationRolesSpan').hide();
                        $('#viewPersonnelLocationRolesDiv').show();
                    }
                    else {
                        $('#viewPersonnelLocationRolesSpan').show();
                        $('#viewPersonnelLocationRolesDiv').hide();
                    }
                });
                if ($("#ViewPersonnelLocation").val() === "2") {
                    $('#viewPersonnelLocationRolesSpan').hide();
                    $('#viewPersonnelLocationRolesDiv').show();
                }
                else {
                    $('#viewPersonnelLocationRolesSpan').show();
                    $('#viewPersonnelLocationRolesDiv').hide();
                }
                initPermRoles("#viewPersonnelLocationRoles", 11);
                $('#LockViewPersonneLocationToGroup').change(function () {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=11&perm=' + $('#ViewPersonnelLocation').val() + '&lockToGroup=' + $('#LockViewPersonneLocationToGroup').is(':checked'),
                        type: 'GET'
                    }).done(function (results) {
                    });
                });

                // View unit location
                $('#ViewUnitLocation').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=12&perm=' + val + '&lockToGroup=' + $('#LockViewPersonneLocationToGroup').is(':checked'),
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#ViewUnitLocation").val() === "2") {
                        $('#viewUnitLocationsRolesSpan').hide();
                        $('#viewUnitLocationsRolesDiv').show();
                    }
                    else {
                        $('#viewUnitLocationsRolesSpan').show();
                        $('#viewUnitLocationsRolesDiv').hide();
                    }
                });
                if ($("#ViewUnitLocation").val() === "2") {
                    $('#viewUnitLocationsRolesSpan').hide();
                    $('#viewUnitLocationsRolesDiv').show();
                }
                else {
                    $('#viewUnitLocationsRolesSpan').show();
                    $('#viewUnitLocationsRolesDiv').hide();
                }
                initPermRoles("#viewUnitLocationsRoles", 12);
                $('#LockViewUnitLocationToGroup').change(function () {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=12&perm=' + $('#ViewUnitLocation').val() + '&lockToGroup=' + $('#LockViewUnitLocationToGroup').is(':checked'),
                        type: 'GET'
                    }).done(function (results) {
                    });
                });

                // Create message
                ////////////////////////////////////////////////////////
                $('#CreateMessage').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=13&perm=' + val,
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#CreateMessage").val() === "2") {
                        $('#createMessagesRolesSpan').hide();
                        $('#createMessagesRolesDiv').show();
                    }
                    else {
                        $('#adjustInventoryRolesSpan').show();
                        $('#createMessagesRolesDiv').hide();
                    }
                });
                if ($("#CreateMessage").val() === "2") {
                    $('#createMessagesRolesSpan').hide();
                    $('#createMessagesRolesDiv').show();
                }
                else {
                    $('#createMessagesRolesSpan').show();
                    $('#createMessagesRolesDiv').hide();
                }

                initPermRoles("#createMessagesRoles", 13);
                ////////////////////////////////////////////////////////

                // View Group Users
                ////////////////////////////////////////////////////////
                $('#ViewGroupsUsers').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=14&perm=' + val,
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#ViewGroupsUsers").val() === "2") {
                        $('#viewUsersRolesSpan').hide();
                        $('#viewUsersRolesDiv').show();
                    }
                    else {
                        $('#viewUsersRolesSpan').show();
                        $('#viewUsersRolesDiv').hide();
                    }
                });
                if ($("#ViewGroupsUsers").val() === "2") {
                    $('#viewUsersRolesSpan').hide();
                    $('#viewUsersRolesDiv').show();
                }
                else {
                    $('#viewUsersRolesSpan').show();
                    $('#viewUsersRolesDiv').hide();
                }
                $('#LockViewGroupsUsersToGroup').change(function () {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=14&perm=' + $('#LockViewGroupsUsersToGroup').val() + '&lockToGroup=' + $('#LockViewGroupsUsersToGroup').is(':checked'),
                        type: 'GET'
                    }).done(function (results) {
                    });
                });

                initPermRoles("#viewUsersRoles", 14);
                ////////////////////////////////////////////////////////

                // Delete Call
                ////////////////////////////////////////////////////////
                $('#DeleteCall').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=15&perm=' + val + '&lockToGroup=' + $('#LockDeleteCallToGroup').is(':checked'),
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#DeleteCall").val() === "2") {
                        $('#deleteCallsRolesSpan').hide();
                        $('#deleteCallsRolesDiv').show();
                    }
                    else {
                        $('#deleteCallsRolesSpan').show();
                        $('#deleteCallsRolesDiv').hide();
                    }
                });
                if ($("#DeleteCall").val() === "2") {
                    $('#deleteCallsRolesSpan').hide();
                    $('#deleteCallsRolesDiv').show();
                }
                else {
                    $('#deleteCallsRolesSpan').show();
                    $('#deleteCallsRolesDiv').hide();
                }
                initPermRoles("#deleteCallsRoles", 15);
                $('#LockDeleteCallToGroup').change(function () {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=15&perm=' + $('#DeleteCall').val() + '&lockToGroup=' + $('#LockDeleteCallToGroup').is(':checked'),
                        type: 'GET'
                    }).done(function (results) {
                    });
                });
                ////////////////////////////////////////////////////////

                // Close Call
                ////////////////////////////////////////////////////////
                $('#CloseCall').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=16&perm=' + val + '&lockToGroup=' + $('#LockCloseCallToGroup').is(':checked'),
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#CloseCall").val() === "2") {
                        $('#closeCallsRolesSpan').hide();
                        $('#closeCallsRolesDiv').show();
                    }
                    else {
                        $('#closeCallsRolesSpan').show();
                        $('#closeCallsRolesDiv').hide();
                    }
                });
                if ($("#CloseCall").val() === "2") {
                    $('#closeCallsRolesSpan').hide();
                    $('#closeCallsRolesDiv').show();
                }
                else {
                    $('#closeCallsRolesSpan').show();
                    $('#closeCallsRolesDiv').hide();
                }
                initPermRoles("#closeCallsRoles", 16);
                $('#LockCloseCallToGroup').change(function () {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=16&perm=' + $('#CloseCall').val() + '&lockToGroup=' + $('#LockCloseCallToGroup').is(':checked'),
                        type: 'GET'
                    }).done(function (results) {
                    });
                });
                ////////////////////////////////////////////////////////

                // Add Call Data
                ////////////////////////////////////////////////////////
                $('#AddCallData').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=17&perm=' + val + '&lockToGroup=' + $('#LockAddCallDataToGroup').is(':checked'),
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#AddCallData").val() === "2") {
                        $('#addCallDataRolesSpan').hide();
                        $('#addCallDataRolesDiv').show();
                    }
                    else {
                        $('#addCallDataRolesSpan').show();
                        $('#addCallDataRolesDiv').hide();
                    }
                });
                if ($("#AddCallData").val() === "2") {
                    $('#addCallDataRolesSpan').hide();
                    $('#addCallDataRolesDiv').show();
                }
                else {
                    $('#addCallDataRolesSpan').show();
                    $('#addCallDataRolesDiv').hide();
                }
                initPermRoles("#addCallDataRoles", 17);
                $('#LockAddCallDataToGroup').change(function () {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=17&perm=' + $('#AddCallData').val() + '&lockToGroup=' + $('#LockAddCallDataToGroup').is(':checked'),
                        type: 'GET'
                    }).done(function (results) {
                    });
                });
                ////////////////////////////////////////////////////////

                // View Groups Units
                ////////////////////////////////////////////////////////
                $('#ViewGroupsUnits').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=18&perm=' + val + '&lockToGroup=' + $('#LockViewGroupsUnitsToGroup').is(':checked'),
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#ViewGroupsUnits").val() === "2") {
                        $('#viewUnitsRolesSpan').hide();
                        $('#viewUnitsRolesDiv').show();
                    }
                    else {
                        $('#viewUnitsRolesSpan').show();
                        $('#viewUnitsRolesDiv').hide();
                    }
                });
                if ($("#ViewGroupsUnits").val() === "2") {
                    $('#viewUnitsRolesSpan').hide();
                    $('#viewUnitsRolesDiv').show();
                }
                else {
                    $('#viewUnitsRolesSpan').show();
                    $('#viewUnitsRolesDiv').hide();
                }
                initPermRoles("#viewUnitsRoles", 18);
                $('#LockViewGroupsUnitsToGroup').change(function () {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=18&perm=' + $('#ViewGroupsUnits').val() + '&lockToGroup=' + $('#LockViewGroupsUnitsToGroup').is(':checked'),
                        type: 'GET'
                    }).done(function (results) {
                    });
                });
                ////////////////////////////////////////////////////////


                // View Contacts (ContactView = 20)
                ////////////////////////////////////////////////////////
                $('#ViewContacts').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=20&perm=' + val + '&lockToGroup=false',
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#ViewContacts").val() === "2") {
                        $('#viewContactsRolesSpan').hide();
                        $('#viewContactsRolesDiv').show();
                    }
                    else {
                        $('#viewContactsRolesSpan').show();
                        $('#viewContactsRolesDiv').hide();
                    }
                });
                if ($("#ViewContacts").val() === "2") {
                    $('#viewContactsRolesSpan').hide();
                    $('#viewContactsRolesDiv').show();
                }
                else {
                    $('#viewContactsRolesSpan').show();
                    $('#viewContactsRolesDiv').hide();
                }
                initPermRoles("#viewContactsRoles", 20);
                ////////////////////////////////////////////////////////


                // Edit Contacts (ContactEdit = 19)
                ////////////////////////////////////////////////////////
                $('#EditContacts').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=19&perm=' + val + '&lockToGroup=false',
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#EditContacts").val() === "2") {
                        $('#editContactsRolesSpan').hide();
                        $('#editContactsRolesDiv').show();
                    }
                    else {
                        $('#editContactsRolesSpan').show();
                        $('#editContactsRolesDiv').hide();
                    }
                });
                if ($("#EditContacts").val() === "2") {
                    $('#editContactsRolesSpan').hide();
                    $('#editContactsRolesDiv').show();
                }
                else {
                    $('#editContactsRolesSpan').show();
                    $('#editContactsRolesDiv').hide();
                }
                initPermRoles("#editContactsRoles", 19);
                ////////////////////////////////////////////////////////


                // Delete Contacts
                ////////////////////////////////////////////////////////
                $('#DeleteContacts').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=21&perm=' + val + '&lockToGroup=false',
                        type: 'GET'
                    }).done(function (results) {
                    });
                    if ($("#DeleteContacts").val() === "2") {
                        $('#deleteContactsRolesSpan').hide();
                        $('#deleteContactsRolesDiv').show();
                    }
                    else {
                        $('#deleteContactsRolesSpan').show();
                        $('#deleteContactsRolesDiv').hide();
                    }
                });
                if ($("#DeleteContacts").val() === "2") {
                    $('#deleteContactsRolesSpan').hide();
                    $('#deleteContactsRolesDiv').show();
                }
                else {
                    $('#deleteContactsRolesSpan').show();
                    $('#deleteContactsRolesDiv').hide();
                }
                initPermRoles("#deleteContactsRoles", 21);
                ////////////////////////////////////////////////////////

                // Create/Edit Workflows
                ////////////////////////////////////////////////////////
                $('#CreateWorkflow').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=22&perm=' + val,
                        type: 'GET'
                    }).done(function (results) { });
                    if (val === "2") {
                        $('#workflowCreateNoRolesSpan').hide();
                        $('#workflowCreateRolesDiv').show();
                    } else {
                        $('#workflowCreateNoRolesSpan').show();
                        $('#workflowCreateRolesDiv').hide();
                    }
                });
                if ($("#CreateWorkflow").val() === "2") {
                    $('#workflowCreateNoRolesSpan').hide();
                    $('#workflowCreateRolesDiv').show();
                } else {
                    $('#workflowCreateNoRolesSpan').show();
                    $('#workflowCreateRolesDiv').hide();
                }
                initPermRoles("#workflowCreateRoles", 22);
                ////////////////////////////////////////////////////////

                // Manage Workflow Credentials
                ////////////////////////////////////////////////////////
                $('#ManageWorkflowCredentials').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=23&perm=' + val,
                        type: 'GET'
                    }).done(function (results) { });
                    if (val === "2") {
                        $('#workflowCredentialsNoRolesSpan').hide();
                        $('#workflowCredentialsRolesDiv').show();
                    } else {
                        $('#workflowCredentialsNoRolesSpan').show();
                        $('#workflowCredentialsRolesDiv').hide();
                    }
                });
                if ($("#ManageWorkflowCredentials").val() === "2") {
                    $('#workflowCredentialsNoRolesSpan').hide();
                    $('#workflowCredentialsRolesDiv').show();
                } else {
                    $('#workflowCredentialsNoRolesSpan').show();
                    $('#workflowCredentialsRolesDiv').hide();
                }
                initPermRoles("#workflowCredentialsRoles", 23);
                ////////////////////////////////////////////////////////

                // View Workflow Runs
                ////////////////////////////////////////////////////////
                $('#ViewWorkflowRuns').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=24&perm=' + val,
                        type: 'GET'
                    }).done(function (results) { });
                    if (val === "2") {
                        $('#workflowRunsNoRolesSpan').hide();
                        $('#workflowRunsRolesDiv').show();
                    } else {
                        $('#workflowRunsNoRolesSpan').show();
                        $('#workflowRunsRolesDiv').hide();
                    }
                });
                if ($("#ViewWorkflowRuns").val() === "2") {
                    $('#workflowRunsNoRolesSpan').hide();
                    $('#workflowRunsRolesDiv').show();
                } else {
                    $('#workflowRunsNoRolesSpan').show();
                    $('#workflowRunsRolesDiv').hide();
                }
                initPermRoles("#workflowRunsRoles", 24);
                ////////////////////////////////////////////////////////

            });
        })(permissions = security.permissions || (security.permissions = {}));
    })(security = resgrid.security || (resgrid.security = {}));
})(resgrid || (resgrid = {}));
