
var resgrid;
(function (resgrid) {
    var workshifts;
    (function (workshifts) {
        var newworkshift;
        (function (newworkshift) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Department - New Workshift');

                $('#Shift_Start').kendoDateTimePicker({
                    interval: 60
                });

                $('#Shift_End').kendoDateTimePicker({
                    interval: 60
                });

                $("#Shift_Color").kendoColorPicker({
                    value: "#72bcd4",
                    buttons: false
                });

                $("#UnitsAssigned").kendoMultiSelect({
                    placeholder: "Select units...",
                    dataTextField: "Name",
                    dataValueField: "UnitId",
                    autoBind: false,
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsList'
                        }
                    }
                });

                var quillDescription = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#newWorkshiftForm', function () {
                    $('#Shift_Description').val(quillDescription.root.innerHTML);
                    
                    return true;
                });
            });
        })(listordering = workshifts.newworkshift || (workshifts.newworkshift = {}));
    })(workshifts = resgrid.workshifts || (resgrid.workshifts = {}));
})(resgrid || (resgrid = {}));
