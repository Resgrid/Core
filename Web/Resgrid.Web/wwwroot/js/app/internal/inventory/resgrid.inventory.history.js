var resgrid;
(function (resgrid) {
    var inventory;
    (function (inventory) {
        var history;
        (function (history) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Inventory - History');
                var strings = typeof inventoryHistoryStrings !== 'undefined' ? inventoryHistoryStrings : {
                    type: 'Type', amount: 'Amount', group: 'Group', unit: 'Unit', batch: 'Batch',
                    timestamp: 'Timestamp', addedBy: 'Added By', actions: 'Actions', view: 'View'
                };
                $("#inventoryList").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Inventory/GetInventoryList', dataSrc: '' },
                    pageLength: 50,
                    columns: [
                        { data: 'Type', title: strings.type, render: resgrid.textCell },
                        { data: 'Amount', title: strings.amount },
                        { data: 'Group', title: strings.group, render: resgrid.textCell },
                        { data: 'Unit', title: strings.unit, render: resgrid.textCell },
                        { data: 'Batch', title: strings.batch, render: resgrid.textCell },
                        { data: 'Timestamp', title: strings.timestamp },
                        { data: 'UserName', title: strings.addedBy, render: resgrid.textCell },
                        {
                            data: 'InventoryId', title: strings.actions, orderable: false,
                            render: function (data) {
                                return '<a class="btn btn-xs btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Inventory/ViewEntry?inventoryId=' + data + '">' + strings.view + '</a>';
                            }
                        }
                    ]
                });
            });
        })(history = inventory.history || (inventory.history = {}));
    })(inventory = resgrid.inventory || (resgrid.inventory = {}));
})(resgrid || (resgrid = {}));
