
var resgrid;
(function (resgrid) {
    var mapping;
    (function (mapping) {
        var importPois;
        (function (importPois) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Import POIs');
                // Native HTML file input is used - no JS initialization needed
            });
        })(importPois = mapping.importPois || (mapping.importPois = {}));
    })(mapping = resgrid.mapping || (resgrid.mapping = {}));
})(resgrid || (resgrid = {}));
