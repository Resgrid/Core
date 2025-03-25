
var resgrid;
(function (resgrid) {
    var inventory;
    (function (inventory) {
        var manageTypes;
        (function (manageTypes) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Inventory - Manage Types');
                $("#typesIndexList").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Inventory/GetTypesList'
                        },
                        schema: {
                            model: {
                                fields: {
                                    TypeId: { type: "number" },
                                    Name: { type: "string" },
                                    ExpiresDays: { type: "string" }
                                }
                            }
                        },
                        pageSize: 50
                    },
                    //height: 400,
                    filterable: true,
                    sortable: true,
                    scrollable: true,
                    pageable: {
                        refresh: true,
                        pageSizes: true,
                        buttonCount: 5
                    },
                    columns: [
                        "Name",
                        {
                            field: "ExpiresDays",
                            title: "Expires Days"
                        },
                        {
                            field: "TypeId",
                            title: "Actions",
                            filterable: false,
                            template: kendo.template($("#typecommand-template").html())
                        }
                    ]
                });
            });
        })(manageTypes = inventory.manageTypes || (inventory.manageTypes = {}));
    })(inventory = resgrid.inventory || (resgrid.inventory = {}));
})(resgrid || (resgrid = {}));
