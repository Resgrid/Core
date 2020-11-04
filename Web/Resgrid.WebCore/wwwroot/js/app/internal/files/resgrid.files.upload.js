
var resgrid;
(function (resgrid) {
    var files;
    (function (files) {
        var upload;
        (function (upload) {
            $(document).ready(function () {
               $("#fileToUpload").kendoUpload({
                    multiple: false,
                    localization: {
                        select: "Select File"
                    }
                });
            });
        })(upload = files.upload || (files.upload = {}));
    })(files = resgrid.files || (resgrid.files = {}));
})(resgrid || (resgrid = {}));
