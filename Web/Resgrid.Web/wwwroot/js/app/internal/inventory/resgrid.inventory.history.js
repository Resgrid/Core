var resgrid;
(function (resgrid) {
    var inventory;
    (function (inventory) {
        var history;
        (function (history) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Inventory - History');
                $("#inventoryList").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Inventory/GetInventoryList', dataSrc: '' },
                    pageLength: 50,
                    columns: [
                        { data: 'Type', title: 'Type' },
                        { data: 'Amount', title: 'Amount' },
                        { data: 'Group', title: 'Group' },
                        { data: 'Batch', title: 'Batch' },
                        { data: 'Timestamp', title: 'Timestamp' },
                        { data: 'UserName', title: 'Added By' },
                        {
                            data: 'InventoryId', title: 'Actions', orderable: false,
                            render: function (data) {
                                return '<a class="btn btn-xs btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Inventory/ViewEntry?inventoryId=' + data + '">View</a>';
                            }
                        }
                    ]
                });
            });
        })(history = inventory.history || (inventory.history = {}));
    })(inventory = resgrid.inventory || (resgrid.inventory = {}));
})(resgrid || (resgrid = {}));
