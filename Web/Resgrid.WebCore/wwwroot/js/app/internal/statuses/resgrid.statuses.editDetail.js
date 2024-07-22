
var resgrid;
(function (resgrid) {
    var statuses;
    (function (statuses) {
        var editdetail;
        (function (editdetail) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Custom Statuses - Edit Detail');
                $("#Detail_ButtonColor").minicolors({
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
                $("#Detail_TextColor").minicolors({
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
