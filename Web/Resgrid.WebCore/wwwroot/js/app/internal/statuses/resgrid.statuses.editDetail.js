
var resgrid;
(function (resgrid) {
    var statuses;
    (function (statuses) {
        var editdetail;
        (function (editdetail) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Custom Statuses - Edit Detail');
                $("#Detail_Order").kendoNumericTextBox({
                    format: "0",
                    min: 0,
                    max: 999,
                    step: 1
                });
                $("#Detail_ButtonColor").kendoColorPicker({
                    value: "#000000",
                    buttons: true
                });
                $("#Detail_TextColor").kendoColorPicker({
                    value: "#ffffff",
                    buttons: true
                });
                $("#Detail_ButtonText").change(function () {
                    $("#previewButton").text($("#Detail_ButtonText").val());
                });
                $("#Detail_ButtonColor").change(function () {
                    $('#previewButton').css('background', $("#Detail_ButtonColor").val());
                });
                $("#Detail_TextColor").change(function () {
                    $('#previewButton').css('color', $("#Detail_TextColor").val());
                });
                $("#Detail_ButtonText").keypress(function (e) {
                    if (String.fromCharCode(e.which).match(/[^A-Za-z0-9_ ]/)) {
                        e.preventDefault();
                    }
                });
            });
        })(editdetail = statuses.editdetail || (statuses.editdetail = {}));
    })(statuses = resgrid.statuses || (resgrid.statuses = {}));
})(resgrid || (resgrid = {}));
