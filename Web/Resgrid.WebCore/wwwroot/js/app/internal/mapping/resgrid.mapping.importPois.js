
var resgrid;
(function (resgrid) {
    var mapping;
    (function (mapping) {
        var importPois;
        (function (importPois) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Import POIs');
                $("#fileToUpload").kendoUpload({
                    multiple: false,
                    localization: {
                        select: "Select (KML/KMZ) File"
                    }
                });
            });
        })(importPois = mapping.importPois || (mapping.importPois = {}));
    })(mapping = resgrid.mapping || (resgrid.mapping = {}));
})(resgrid || (resgrid = {}));
