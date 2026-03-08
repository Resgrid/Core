var resgrid;
(function (resgrid) {
    var inventory;
    (function (inventory) {
        var manageTypes;
        (function (manageTypes) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Inventory - Manage Types');
                var strings = typeof inventoryTypesStrings !== 'undefined' ? inventoryTypesStrings : {
                    name: 'Name', expiresDays: 'Expires Days', actions: 'Actions', edit: 'Edit', delete: 'Delete'
                };
                $("#typesIndexList").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Inventory/GetTypesList', dataSrc: '' },
                    pageLength: 50,
                    columns: [
                        { data: 'Name', title: strings.name },
                        { data: 'ExpiresDays', title: strings.expiresDays },
                        {
                            data: 'TypeId', title: strings.actions, orderable: false,
                            render: function (data) {
                                return '<a class="btn btn-xs btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Inventory/EditType?typeId=' + data + '">' + strings.edit + '</a> ' +
                                       '<a class="btn btn-xs btn-danger" href="' + resgrid.absoluteBaseUrl + '/User/Inventory/DeleteType?typeId=' + data + '">' + strings.delete + '</a>';
                            }
                        }
                    ]
                });
            });
        })(manageTypes = inventory.manageTypes || (inventory.manageTypes = {}));
    })(inventory = resgrid.inventory || (resgrid.inventory = {}));
})(resgrid || (resgrid = {}));
