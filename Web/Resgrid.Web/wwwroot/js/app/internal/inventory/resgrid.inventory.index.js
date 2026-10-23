var resgrid;
(function (resgrid) {
    var inventory;
    (function (inventory) {
        var index;
        (function (index) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Inventory List');
                var strings = typeof inventoryIndexStrings !== 'undefined' ? inventoryIndexStrings : {
                    name: 'Name', group: 'Group', unit: 'Unit', count: 'Count'
                };
                $("#inventoryIndexList").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Inventory/GetCombinedInventoryList', dataSrc: '' },
                    pageLength: 50,
                    columns: [
                        { data: 'Name', title: strings.name, render: resgrid.textCell },
                        { data: 'Group', title: strings.group, render: resgrid.textCell },
                        { data: 'Unit', title: strings.unit, render: resgrid.textCell },
                        { data: 'Count', title: strings.count }
                    ]
                });
            });
        })(index = inventory.index || (inventory.index = {}));
    })(inventory = resgrid.inventory || (resgrid.inventory = {}));
})(resgrid || (resgrid = {}));
