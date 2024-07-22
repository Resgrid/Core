
var resgrid;
(function (resgrid) {
    var units;
    (function (units) {
        var unitlog;
        (function (unitlog) {
            $(document).ready(function () {
                $("#Log_Timestamp").datetimepicker({ step: 15 });

                let quill = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#addUnitLogForm', function () {
                    $('#Log_Narrative').val(quill.root.innerHTML);

                    return true;
                });
            });
        })(unitlog = units.unitlog || (units.unitlog = {}));
    })(units = resgrid.units || (resgrid.units = {}));
})(resgrid || (resgrid = {}));
