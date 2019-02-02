
var resgrid;
(function (resgrid) {
    var statuses;
    (function (statuses) {
        var editstatus;
        (function (editstatus) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Custom Statuses - Edit');
                $("#editor").kendoEditor();
                $("#buttonColor").kendoColorPicker({
                    value: "#000000"
                });
                $("#textColor").kendoColorPicker({
                    value: "#FF5733"
                });
                $("#buttonText").change(function () {
                    $("#previewButton").text($("#buttonText").val());
                });
                $("#buttonColor").change(function () {
                    $('#previewButton').css('background', $("#buttonColor").val());
                });
                $("#textColor").change(function () {
                    $('#previewButton').css('color', $("#textColor").val());
                });
                $("#buttonText").keypress(function (e) {
                    if (String.fromCharCode(e.which).match(/[^A-Za-z0-9_ ]/)) {
                        e.preventDefault();
                    }
                });
                $(".numberEntry").keypress(function (e) {
                    if (String.fromCharCode(e.which).match(/^\d+$/)) {
                        e.preventDefault();
                    }
                });
                $('#newStatusModal').on('show.bs.modal', function (event) {
                    $('#buttonText').val('');
                    $('#buttonColor').val('#000000');
                    $('#textColor').val('#FF5733');
                    $('#requireGps').prop('checked', false);
                    $('#detailType').val('0');
                    $('#noteType').val('0');
                    $("#previewButton").text("Preview Button");
                    $('#previewButton').css('background', "#000000");
                    $('#previewButton').css('color', "#FF5733");
                    var colorPicker = $('#buttonColor').data("kendoColorPicker");
                    colorPicker.value('#000000');
                    var colorPickerText = $('#textColor').data("kendoColorPicker");
                    colorPickerText.value('#FF5733');
                });
            });
            function addOption() {
                $('#newStatusModal').modal('hide');
                resgrid.statuses.editstatus.optionsCount++;
                $('#options tbody').first().append("<tr><td><input type='number' min='0' id='order_" + editstatus.optionsCount + "' name='order_" + editstatus.optionsCount + "' value='0' class='numberEntry'></td><td>" + $('#buttonText').val() + "<input type='hidden' id='buttonText_" + editstatus.optionsCount + "' name='buttonText_" + editstatus.optionsCount + "' value='" + $('#buttonText').val() + "'></input></td><td><a class='btn btn-default' role='button' style='color:" + $('#textColor').val() + ";background:" + $('#buttonColor').val() + ";'>" + $('#buttonText').val() + "</a><input type='hidden' id='buttonColor_" + editstatus.optionsCount + "' name='buttonColor_" + editstatus.optionsCount + "' value='" + $('#buttonColor').val() + "'><input type='hidden' id='textColor_" + editstatus.optionsCount + "' name='textColor_" + editstatus.optionsCount + "' value='" + $('#textColor').val() + "'><input type='hidden' id='detailType_" + editstatus.optionsCount + "' name='detailType_" + editstatus.optionsCount + "' value='" + $('#detailType').val() + "'></input><input type='hidden' id='noteType_" + editstatus.optionsCount + "' name='noteType_" + editstatus.optionsCount + "' value='" + $('#noteType').val() + "'></input><input type='hidden' id='requireGps_" + editstatus.optionsCount + "' name='requireGps_" + editstatus.optionsCount + "' value='" + $('#requireGps').val() + "'></input></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='btn btn-xs btn-danger' data-original-title='Remove this option'>Remove</a></td></tr>");
            }
            editstatus.addOption = addOption;
            function isNumber(evt) {
                evt = (evt) ? evt : window.event;
                var charCode = (evt.which) ? evt.which : evt.keyCode;
                if (charCode > 31 && (charCode < 48 || charCode > 57)) {
                    return false;
                }
                return true;
            }
            editstatus.isNumber = isNumber;
        })(editstatus = statuses.editstatus || (statuses.editstatus = {}));
    })(statuses = resgrid.statuses || (resgrid.statuses = {}));
})(resgrid || (resgrid = {}));
