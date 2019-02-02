
var resgrid;
(function (resgrid) {
    var inventory;
    (function (inventory) {
        var history;
        (function (history) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Inventory - History');
                $("#inventoryList").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Inventory/GetInventoryList'
                        },
                        schema: {
                            model: {
                                fields: {
                                    InventoryId: { type: "number" },
                                    Type: { type: "string" },
                                    Amount: { type: "number" },
                                    Group: { type: "string" },
                                    Unit: { type: "string" },
                                    Batch: { type: "string" },
                                    Timestamp: { type: "string" },
                                    UserName: { type: "string" }
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
                        "Type",
                        "Amount",
                        "Group",
                        "Batch",
                        "Timestamp",
                        {
                            field: "UserName",
                            title: "Added By"
                        },
                        {
                            field: "InventoryId",
                            title: "Actions",
                            filterable: false,
                            template: kendo.template($("#inventorycommand-template").html())
                        }
                    ]
                });
            });
        })(history = inventory.history || (inventory.history = {}));
    })(inventory = resgrid.inventory || (resgrid.inventory = {}));
})(resgrid || (resgrid = {}));
