
var resgrid;
(function (resgrid) {
    var groups;
    (function (groups) {
        var editgroup;
        (function (editgroup) {
            $(document).ready(function () {
                $("#groupAdmins").kendoMultiSelect({
                    placeholder: "Select group admins...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterNotInGroup=true&ignoreGroupId=' + $('#EditGroup_DepartmentGroupId').val()
                        }
                    }
                });
                $("#groupUsers").kendoMultiSelect({
                    placeholder: "Select group users...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterNotInGroup=true&ignoreGroupId=' + $('#EditGroup_DepartmentGroupId').val()
                        }
                    }
                });
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Groups/GetMembersForGroup?groupId=' + $('#EditGroup_DepartmentGroupId').val() + '&includeAdmins=true&includeNormal=false',
                    contentType: 'application/json',
                    type: 'GET'
                }).done(function (data) {
                    if (data) {
                        var multiSelect = $("#groupAdmins").data("kendoMultiSelect");
                        var valuesToAdd = [];
                        for (var i = 0; i < data.length; i++) {
                            valuesToAdd.push(data[i].UserId);
                        }
                        multiSelect.value(valuesToAdd);
                    }
                });
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Groups/GetMembersForGroup?groupId=' + $('#EditGroup_DepartmentGroupId').val() + '&includeAdmins=false&includeNormal=true',
                    contentType: 'application/json',
                    type: 'GET'
                }).done(function (data) {
                    if (data) {
                        var multiSelect = $("#groupUsers").data("kendoMultiSelect");
                        var valuesToAdd = [];
                        for (var i = 0; i < data.length; i++) {
                            valuesToAdd.push(data[i].UserId);
                        }
                        multiSelect.value(valuesToAdd);
                    }
                });
            });
        })(editgroup = groups.editgroup || (groups.editgroup = {}));
    })(groups = resgrid.groups || (resgrid.groups = {}));
})(resgrid || (resgrid = {}));
