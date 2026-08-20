
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
            var unitsRequestSequence = 0;
            function getUnits(stationId) {
                var noUnitLabel = (typeof inventoryAdjustStrings !== 'undefined' && inventoryAdjustStrings.noUnit) ? inventoryAdjustStrings.noUnit : 'No Unit';
                var requestId = ++unitsRequestSequence;
                function resetUnits() {
                    $('#UnitId').empty();
                    $('#UnitId').append('<option value="0">' + noUnitLabel + '</option>');
                }
                var groupId = parseInt(stationId, 10);
                if (isNaN(groupId) || groupId <= 0) {
                    resetUnits();
                    return;
                }
                // Clear before the request so the previous group's units are never
                // selectable while it is in flight, nor left behind if it fails or
                // answers with something other than a unit array. Submitting an
                // adjustment against a unit from the wrong station must not be possible.
                resetUnits();
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsForGroup?groupId=' + groupId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    // A newer group change already reset the selector; this response is
                    // for a group the user has moved off of, so applying it would restore
                    // exactly the stale options the up-front clear removed.
                    if (requestId !== unitsRequestSequence) {
                        return;
                    }
                    if (data && $.isArray(data)) {
                        resetUnits();
                        $.each(data, function (index, value) {
                            $('#UnitId').append('<option value="' + data[index].UnitId + '">' + data[index].Name + '</option>');
                        });
                    }
                }).fail(function () {
                    if (requestId !== unitsRequestSequence) {
                        return;
                    }
                    resetUnits();
                });
            }
            adjust.getUnits = getUnits;
        })(adjust = inventory.adjust || (inventory.adjust = {}));
    })(inventory = resgrid.inventory || (resgrid.inventory = {}));
})(resgrid || (resgrid = {}));
