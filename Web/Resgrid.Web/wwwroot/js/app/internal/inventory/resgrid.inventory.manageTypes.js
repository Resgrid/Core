var resgrid;
(function (resgrid) {
    var inventory;
    (function (inventory) {
        var manageTypes;
        (function (manageTypes) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Inventory - Manage Types');
                $("#typesIndexList").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Inventory/GetTypesList', dataSrc: '' },
                    pageLength: 50,
                    columns: [
                        { data: 'Name', title: 'Name' },
                        { data: 'ExpiresDays', title: 'Expires Days' },
                        {
                            data: 'TypeId', title: 'Actions', orderable: false,
                            render: function (data) {
                                return '<a class="btn btn-xs btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Inventory/EditType?typeId=' + data + '">Edit</a> ' +
                                       '<a class="btn btn-xs btn-danger" href="' + resgrid.absoluteBaseUrl + '/User/Inventory/DeleteType?typeId=' + data + '">Delete</a>';
                            }
                        }
                    ]
                });
            });
        })(manageTypes = inventory.manageTypes || (inventory.manageTypes = {}));
    })(inventory = resgrid.inventory || (resgrid.inventory = {}));
})(resgrid || (resgrid = {}));
