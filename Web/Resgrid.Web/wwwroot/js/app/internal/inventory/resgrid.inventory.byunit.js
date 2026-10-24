var resgrid;
(function (resgrid) {
    var inventory;
    (function (inventory) {
        var byunit;
        (function (byunit) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Inventory - By Unit');
                var strings = typeof inventoryByUnitStrings !== 'undefined' ? inventoryByUnitStrings : {
                    unit: 'Unit', type: 'Type', amount: 'Amount', group: 'Group', batch: 'Batch',
                    timestamp: 'Timestamp', addedBy: 'Added By', actions: 'Actions', view: 'View'
                };
                $("#inventoryByUnitList").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Inventory/GetInventoryByUnitList', dataSrc: '' },
                    pageLength: 50,
                    order: [[0, 'asc']],
                    columns: [
                        { data: 'Unit', title: strings.unit, render: resgrid.textCell },
                        { data: 'Type', title: strings.type, render: resgrid.textCell },
                        { data: 'Amount', title: strings.amount },
                        { data: 'Group', title: strings.group, render: resgrid.textCell },
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
        })(byunit = inventory.byunit || (inventory.byunit = {}));
    })(inventory = resgrid.inventory || (resgrid.inventory = {}));
})(resgrid || (resgrid = {}));
