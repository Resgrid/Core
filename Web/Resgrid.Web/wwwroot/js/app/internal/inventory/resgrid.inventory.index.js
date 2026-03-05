var resgrid;
(function (resgrid) {
    var inventory;
    (function (inventory) {
        var index;
        (function (index) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Inventory List');
                $("#inventoryIndexList").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Inventory/GetCombinedInventoryList', dataSrc: '' },
                    pageLength: 50,
                    columns: [
                        { data: 'Name', title: 'Name' },
                        { data: 'Group', title: 'Group' },
                        { data: 'Unit', title: 'Unit' },
                        { data: 'Count', title: 'Count' }
                    ]
                });
            });
        })(index = inventory.index || (inventory.index = {}));
    })(inventory = resgrid.inventory || (resgrid.inventory = {}));
})(resgrid || (resgrid = {}));
