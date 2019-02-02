
var resgrid;
(function (resgrid) {
    var notes;
    (function (notes) {
        var newnote;
        (function (newnote) {
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
        })(newnote = notes.newnote || (notes.newnote = {}));
    })(notes = resgrid.notes || (resgrid.notes = {}));
})(resgrid || (resgrid = {}));
