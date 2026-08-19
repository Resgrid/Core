
var resgrid;
(function (resgrid) {
    var inventory;
    (function (inventory) {
        var adjust;
        (function (adjust) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Inventory - Adjust');
                $('select').select2();
                $('#Inventory_GroupId').on("change", function (e) { getUnits($(this).val()); });
                getUnits($('#Inventory_GroupId').val());
                $("#Inventory_Amount").attr({ type: 'number', min: -999999999, max: 999999999, step: 1 });
            });
            function getUnits(stationId) {
                var noUnitLabel = (typeof inventoryAdjustStrings !== 'undefined' && inventoryAdjustStrings.noUnit) ? inventoryAdjustStrings.noUnit : 'No Unit';
                var groupId = parseInt(stationId, 10);
                if (isNaN(groupId) || groupId <= 0) {
                    $('#UnitId').empty();
                    $('#UnitId').append('<option value="0">' + noUnitLabel + '</option>');
                    return;
                }
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsForGroup?groupId=' + groupId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    if (data && $.isArray(data)) {
                        $('#UnitId').empty();
                        $('#UnitId').append('<option value="0">' + noUnitLabel + '</option>');
                        $.each(data, function (index, value) {
                            $('#UnitId').append('<option value="' + data[index].UnitId + '">' + data[index].Name + '</option>');
                        });
                    }
                });
            }
            adjust.getUnits = getUnits;
        })(adjust = inventory.adjust || (inventory.adjust = {}));
    })(inventory = resgrid.inventory || (resgrid.inventory = {}));
})(resgrid || (resgrid = {}));
