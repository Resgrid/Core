
var resgrid;
(function (resgrid) {
    var personnel;
    (function (personnel) {
        var index;
        (function (index) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Personnel List');
                $("#personnelIndexList").kendoGrid({
                    dataSource: {
                        //type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelList'
                        },
                        schema: {
                            model: {
                                fields: {
                                    Name: { type: "string" },
                                    EmailAddress: { type: "string" },
                                    Group: { type: "string" },
                                    State: { type: "string" },
                                    UserId: { type: "string" },
                                    Roles: { type: "string" }
                                }
                            }
                        },
                        pageSize: 50
                    },
                    filterable: true,
                    sortable: true,
                    scrollable: true,
                    //dataBound: function () {  },
                    pageable: {
                        refresh: true,
                        pageSizes: true,
                        buttonCount: 5
                    },
                    columns: [
                        {
                            field: "Name",
                            title: "Name"
                        },
                        {
                            field: "Group",
                            title: "Group"
                        },
                        {
                            field: "Roles",
                            title: "Roles"
                        },
                        {
                            field: "State",
                            title: "State",
                            width: 130
                        },
                        {
                            field: "UserId",
                            title: "Actions",
                            filterable: false,
                            sortable: false,
                            width: 220,
                            template: kendo.template($("#command-template").html())
                        }
                    ]
                });
            });
        })(index = personnel.index || (personnel.index = {}));
    })(personnel = resgrid.personnel || (resgrid.personnel = {}));
})(resgrid || (resgrid = {}));
