
var resgrid;
(function (resgrid) {
    var groups;
    (function (groups) {
        var newgroup;
        (function (newgroup) {
            $(document).ready(function () {
                if ($('.groupTypeOrgRadio').is(':checked')) {
                    $("#ParentFields").show();
                    $("#StationAddress").hide();
                }
                else if ($('.groupTypeStationRadio').is(':checked')) {
                    $("#ParentFields").hide();
                    $("#StationAddress").show();
                }
                else {
                    $("#ParentFields").hide();
                    $("#StationAddress").hide();
                }
                $("#groupAdmins").kendoMultiSelect({
                    placeholder: "Select group admins...",
                    dataTextField: "Name",
                    dataValueField: "Id",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterNotInGroup=true'
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
                            read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterNotInGroup=true'
                        }
                    }
                });
            });
            function showParentFields() {
                $("#ParentFields").show();
                $("#StationAddress").hide();
            }
            newgroup.showParentFields = showParentFields;
            function showAddressFields() {
                $("#ParentFields").hide();
                $("#StationAddress").show();
            }
            newgroup.showAddressFields = showAddressFields;
        })(newgroup = groups.newgroup || (groups.newgroup = {}));
    })(groups = resgrid.groups || (resgrid.groups = {}));
})(resgrid || (resgrid = {}));
