
var resgrid;
(function (resgrid) {
    var documents;
    (function (documents) {
        var newdocument;
        (function (newdocument) {
            $(document).ready(function () {
                $("#Document_Description").kendoEditor();
                $("#fileToUpload").kendoUpload({
                    multiple: false,
                    localization: {
                        select: "Select File"
                    }
                });
                $("#Category").kendoComboBox({
                    minLength: 3,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Documents/GetDepartmentDocumentCategories'
                        }
                    },
                    filter: "contains",
                    suggest: true
                });
            });
        })(newdocument = documents.newdocument || (documents.newdocument = {}));
    })(documents = resgrid.documents || (resgrid.documents = {}));
})(resgrid || (resgrid = {}));
