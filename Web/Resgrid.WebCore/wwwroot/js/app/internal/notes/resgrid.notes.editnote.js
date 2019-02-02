
var resgrid;
(function (resgrid) {
    var notes;
    (function (notes) {
        var editnote;
        (function (editnote) {
            $(document).ready(function () {
                $("#Body").kendoEditor();
                $("#Category").kendoComboBox({
                    minLength: 3,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Notes/GetDepartmentNotesCategories'
                        }
                    },
                    filter: "contains",
                    suggest: true
                });
            });
        })(editnote = notes.editnote || (notes.editnote = {}));
    })(notes = resgrid.notes || (resgrid.notes = {}));
})(resgrid || (resgrid = {}));
