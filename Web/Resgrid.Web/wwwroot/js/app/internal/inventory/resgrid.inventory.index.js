
var resgrid;
(function (resgrid) {
    var inventory;
    (function (inventory) {
        var index;
        (function (index) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Inventory List');
                $("#inventoryIndexList").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Inventory/GetCombinedInventoryList'
                        },
                        schema: {
                            model: {
                                fields: {
                                    Name: { type: "string" },
                                    Group: { type: "string" },
                                    Unit: { type: "string" },
                                    Count: { type: "number" }
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
                        "Group",
                        "Unit",
                        "Count"
                    ]
                });
            });
        })(index = inventory.index || (inventory.index = {}));
    })(inventory = resgrid.inventory || (resgrid.inventory = {}));
})(resgrid || (resgrid = {}));
