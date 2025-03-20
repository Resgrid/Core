
var resgrid;
(function (resgrid) {
    var inventory;
    (function (inventory) {
        var adjust;
        (function (adjust) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Inventory - Adjust');
                $('select').select2();
                $('#Inventory_GroupId').on("change", function (e) { getUnits(e.val); });
                getUnits($('#Inventory_GroupId').val());
                $("#Inventory_Amount").kendoNumericTextBox({
                    format: "#",
                    min: -999999999,
                    max: 999999999,
                    step: 1
                });
            });
            function getUnits(stationId) {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsForGroup?groupId=' + stationId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    if (data) {
                        $('#UnitId').empty();
                        $('#UnitId').append('<option value="0">No Unit</option>');
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
