
var resgrid;
(function (resgrid) {
    var inventory;
    (function (inventory) {
        var addType;
        (function (addType) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Inventory - Add Type');
                $("#Type_ExpiresDays").kendoNumericTextBox({
                    format: "#",
                    min: 0,
                    max: 1825,
                    step: 1
                });
            });
        })(addType = inventory.addType || (inventory.addType = {}));
    })(inventory = resgrid.inventory || (resgrid.inventory = {}));
})(resgrid || (resgrid = {}));
