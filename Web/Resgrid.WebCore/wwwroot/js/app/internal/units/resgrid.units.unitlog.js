
var resgrid;
(function (resgrid) {
    var units;
    (function (units) {
        var unitlog;
        (function (unitlog) {
            $(document).ready(function () {
                $("#Log_Timestamp").kendoDateTimePicker();
                $("#Log_Narrative").kendoEditor();
            });
        })(unitlog = units.unitlog || (units.unitlog = {}));
    })(units = resgrid.units || (resgrid.units = {}));
})(resgrid || (resgrid = {}));
