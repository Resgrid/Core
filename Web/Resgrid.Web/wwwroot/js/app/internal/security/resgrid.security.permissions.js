
var resgrid;
(function (resgrid) {
    var security;
    (function (security) {
        var permissions;
        (function (permissions) {
            // Every SetPermission/SetPermissionData call is a state-changing POST carrying the
            // page's antiforgery token (rendered by the Security Index view). Reads stay GET.
            function antiForgeryToken() {
                return $('input[name="__RequestVerificationToken"]').first().val();
            }
            // lockValue (optional) returns the row's current LockToGroup so a role change does not
            // silently reset the group lock; rows without a lock checkbox omit it.
            function initPermRoles(selector, permType, lockValue) {
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
                    var url = resgrid.absoluteBaseUrl + '/User/Security/SetPermissionData?type=' + permType + '&data=' + encodeURIComponent(($(selector).val() || []).join(','));
                    if (typeof lockValue === 'function') { url += '&lockToGroup=' + lockValue(); }
                    $.ajax({
                        url: url,
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
                    }).fail(function () {
                        // A rejected write leaves select2 showing roles the server never stored, so the
                        // failure has to be surfaced rather than swallowed as an unhandled rejection.
                        resgrid.common.notifications.showError('Permission Not Saved', 'The permission change could not be saved. Reload the page and try again.');
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
                    }).done(function (results) {
                    });
                });
                $('#RemoveUsers').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=1&perm=' + val,
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
                    }).done(function (results) {
                    });
                });

                // View unit location
                $('#ViewUnitLocation').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=12&perm=' + val + '&lockToGroup=' + $('#LockViewPersonneLocationToGroup').is(':checked'),
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
                    }).done(function (results) {
                    });
                });

                // Create message
                ////////////////////////////////////////////////////////
                $('#CreateMessage').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=13&perm=' + val,
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
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

                // Use Calendar Sync
                ////////////////////////////////////////////////////////
                $('#UseCalendarSync').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=28&perm=' + val,
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
                    }).done(function (results) {
                    });
                    if ($("#UseCalendarSync").val() === "2") {
                        $('#calSyncNoRolesSpan').hide();
                        $('#calSyncRolesDiv').show();
                    } else {
                        $('#calSyncNoRolesSpan').show();
                        $('#calSyncRolesDiv').hide();
                    }
                });
                if ($("#UseCalendarSync").val() === "2") {
                    $('#calSyncNoRolesSpan').hide();
                    $('#calSyncRolesDiv').show();
                } else {
                    $('#calSyncNoRolesSpan').show();
                    $('#calSyncRolesDiv').hide();
                }
                initPermRoles("#calSyncRoles", 28);
                ////////////////////////////////////////////////////////

                // Dispatch App Login
                ////////////////////////////////////////////////////////
                $('#DispatchAppLogin').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=29&perm=' + val,
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
                    }).done(function (results) {
                    });
                    if ($("#DispatchAppLogin").val() === "2") {
                        $('#dispatchAppLoginNoRolesSpan').hide();
                        $('#dispatchAppLoginRolesDiv').show();
                    } else {
                        $('#dispatchAppLoginNoRolesSpan').show();
                        $('#dispatchAppLoginRolesDiv').hide();
                    }
                });
                if ($("#DispatchAppLogin").val() === "2") {
                    $('#dispatchAppLoginNoRolesSpan').hide();
                    $('#dispatchAppLoginRolesDiv').show();
                } else {
                    $('#dispatchAppLoginNoRolesSpan').show();
                    $('#dispatchAppLoginRolesDiv').hide();
                }
                initPermRoles("#dispatchAppLoginRoles", 29);
                ////////////////////////////////////////////////////////

                // Command App Login
                ////////////////////////////////////////////////////////
                $('#CommandAppLogin').change(function () {
                    var val = this.value;
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=30&perm=' + val,
                        type: 'POST',
                        headers: { 'RequestVerificationToken': antiForgeryToken() }
                    }).done(function (results) {
                    });
                    if ($("#CommandAppLogin").val() === "2") {
                        $('#commandAppLoginNoRolesSpan').hide();
                        $('#commandAppLoginRolesDiv').show();
                    } else {
                        $('#commandAppLoginNoRolesSpan').show();
                        $('#commandAppLoginRolesDiv').hide();
                    }
                });
                if ($("#CommandAppLogin").val() === "2") {
                    $('#commandAppLoginNoRolesSpan').hide();
                    $('#commandAppLoginRolesDiv').show();
                } else {
                    $('#commandAppLoginNoRolesSpan').show();
                    $('#commandAppLoginRolesDiv').hide();
                }
                initPermRoles("#commandAppLoginRoles", 30);
                ////////////////////////////////////////////////////////

                // Advanced Data Protection permissions (PermissionTypes 31-39)
                ////////////////////////////////////////////////////////
                var adpPermissions = [
                    { sel: '#ManageDataProtection', type: 31, roles: '#adpManageRoles', span: '#adpManageNoRolesSpan', div: '#adpManageRolesDiv' },
                    { sel: '#ViewProtectedCallData', type: 32, roles: '#adpViewCallRoles', span: '#adpViewCallNoRolesSpan', div: '#adpViewCallRolesDiv' },
                    { sel: '#EditProtectedCallData', type: 33, roles: '#adpEditCallRoles', span: '#adpEditCallNoRolesSpan', div: '#adpEditCallRolesDiv' },
                    { sel: '#ViewProtectedPersonnelData', type: 34, roles: '#adpViewPersonnelRoles', span: '#adpViewPersonnelNoRolesSpan', div: '#adpViewPersonnelRolesDiv' },
                    { sel: '#ViewProtectedContactData', type: 35, roles: '#adpViewContactRoles', span: '#adpViewContactNoRolesSpan', div: '#adpViewContactRolesDiv' },
                    { sel: '#ViewProtectedOperationalData', type: 36, roles: '#adpViewOperationalRoles', span: '#adpViewOperationalNoRolesSpan', div: '#adpViewOperationalRolesDiv' },
                    { sel: '#ExportProtectedData', type: 37, roles: '#adpExportRoles', span: '#adpExportNoRolesSpan', div: '#adpExportRolesDiv' },
                    { sel: '#ConfigureProtectedDataEgress', type: 38, roles: '#adpEgressRoles', span: '#adpEgressNoRolesSpan', div: '#adpEgressRolesDiv' },
                    { sel: '#BreakGlassProtectedData', type: 39, roles: '#adpBreakGlassRoles', span: '#adpBreakGlassNoRolesSpan', div: '#adpBreakGlassRolesDiv' }
                ];
                adpPermissions.forEach(function (p) {
                    var toggleRoles = function () {
                        if ($(p.sel).val() === "2") {
                            $(p.span).hide();
                            $(p.div).show();
                        } else {
                            $(p.span).show();
                            $(p.div).hide();
                        }
                    };
                    $(p.sel).change(function () {
                        $.ajax({
                            url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=' + p.type + '&perm=' + this.value,
                            type: 'POST',
                            headers: { 'RequestVerificationToken': antiForgeryToken() }
                        }).done(function (results) {
                        });
                        toggleRoles();
                    });
                    toggleRoles();
                    initPermRoles(p.roles, p.type);
                });
                ////////////////////////////////////////////////////////

                // Records (RMS) permissions (PermissionTypes 50-67). Rows are rendered from
                // RecordPermissionCatalog, so the wiring reads each row's data attributes instead of a
                // fixed list. Values 2 and 4 both take selected roles; rows with a lock checkbox send it
                // on every write so a dropdown or role change never resets the group lock.
                ////////////////////////////////////////////////////////
                $('tr[data-record-perm]').each(function () {
                    var row = $(this);
                    var type = row.data('record-perm');
                    var el = row.data('record-el');
                    var sel = '#' + el;
                    var lock = '#Lock_' + el;
                    var roles = '#' + el + 'Roles';
                    var span = '#' + el + 'NoRolesSpan';
                    var div = '#' + el + 'RolesDiv';
                    var lockValue = function () { return $(lock).length ? $(lock).is(':checked') : false; };
                    var toggleRoles = function () {
                        var v = $(sel).val();
                        if (v === "2" || v === "4") {
                            $(span).hide();
                            $(div).show();
                        } else {
                            $(span).show();
                            $(div).hide();
                        }
                    };
                    var postAction = function () {
                        return $.ajax({
                            url: resgrid.absoluteBaseUrl + '/User/Security/SetPermission?type=' + type + '&perm=' + $(sel).val() + '&lockToGroup=' + lockValue(),
                            type: 'POST',
                            headers: { 'RequestVerificationToken': antiForgeryToken() }
                        });
                    };
                    var postRoles = function () {
                        return $.ajax({
                            url: resgrid.absoluteBaseUrl + '/User/Security/SetPermissionData?type=' + type + '&data=' + encodeURIComponent(($(roles).val() || []).join(',')) + '&lockToGroup=' + lockValue(),
                            type: 'POST',
                            headers: { 'RequestVerificationToken': antiForgeryToken() }
                        });
                    };
                    $(sel).change(function () {
                        // SetPermission clears the stored roles; re-apply the ones still selected on screen.
                        postAction().done(function () {
                            if (($(roles).val() || []).length > 0) { postRoles(); }
                        });
                        toggleRoles();
                    });
                    $(lock).change(function () { postRoles(); });
                    toggleRoles();
                    initPermRoles(roles, type, lockValue);
                });
                ////////////////////////////////////////////////////////

            });
        })(permissions = security.permissions || (security.permissions = {}));
    })(security = resgrid.security || (resgrid.security = {}));
})(resgrid || (resgrid = {}));
