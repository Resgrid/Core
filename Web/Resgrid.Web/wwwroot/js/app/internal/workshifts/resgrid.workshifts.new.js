var resgrid;
(function (resgrid) {
    var workshifts;
    (function (workshifts) {
        var newworkshift;
        (function (newworkshift) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Department - New Workshift');

                $('#Shift_Start').datetimepicker({ step: 60 });

                $('#Shift_End').datetimepicker({ step: 60 });

                $("#Shift_Color").minicolors({
                    animationSpeed: 50,
                    animationEasing: 'swing',
                    changeDelay: 0,
                    control: 'hue',
                    defaultValue: '#0080ff',
                    format: 'hex',
                    showSpeed: 100,
                    hideSpeed: 100,
                    inline: false,
                    theme: 'bootstrap'
                });

                $("#UnitsAssigned").select2({
                    placeholder: "Select units...",
                    allowClear: true,
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsList',
                        dataType: 'json',
                        processResults: function (data) {
                            return {
                                results: $.map(data, function (item) {
                                    return { id: item.UnitId, text: item.Name };
                                })
                            };
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
        })(newworkshift = workshifts.newworkshift || (workshifts.newworkshift = {}));
    })(workshifts = resgrid.workshifts || (resgrid.workshifts = {}));
})(resgrid || (resgrid = {}));
