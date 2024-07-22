
var resgrid;
(function (resgrid) {
    var statuses;
    (function (statuses) {
        var newstatus;
        (function (newstatus) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Custom Statuses - New');
                resgrid.statuses.newstatus.optionsCount = 0;

                let quill = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#newCustomStatusesForm', function () {
                    $('#State_Description').val(quill.root.innerHTML);

                    return true;
                });

                $("#buttonColor").minicolors({
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

                $("#textColor").minicolors({
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

                $("#buttonText").keypress(function (e) {
                    if (String.fromCharCode(e.which).match(/[^A-Za-z0-9_ ]/)) {
                        e.preventDefault();
                    }
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
                    $('#baseType').val('-1');
                    $("#previewButton").text("Preview Button");
                    $('#previewButton').css('background', "#000000");
                    $('#previewButton').css('color', "#FF5733");
                    $('#buttonColor').minicolors('value', '#000000');
                    $('#textColor').minicolors('value', '#FF5733');
                });
            });
            function addOption() {
                var buttonText = $('#buttonText').val();
                if (!buttonText) {
                    $("#addOptionErrors").text('You need to specify a button text');
                    $("#addOptionErrors").show();
                }
                else {
                    $('#newStatusModal').modal('hide');
                    $("#addOptionErrors").hide();
                    resgrid.statuses.newstatus.optionsCount++;
                    $('#options tbody').first().append("<tr><td><input type='number' min='0' id='order_" + newstatus.optionsCount + "' name='order_" + newstatus.optionsCount + "' value='0' onkeypress='return resgrid.statuses.newstatus.isNumber(event)'></td><td>" + $('#buttonText').val() + "<input type='hidden' id='buttonText_" + newstatus.optionsCount + "' name='buttonText_" + newstatus.optionsCount + "' value='" + $('#buttonText').val() + "'></input><input type='hidden' id='baseType_" + newstatus.optionsCount + "' name='baseType_" + newstatus.optionsCount + "' value='" + $('#baseType').val() + "'></input></td><td><a class='btn btn-default' role='button' style='color:" + $('#textColor').val() + ";background:" + $('#buttonColor').val() + ";'>" + $('#buttonText').val() + "</a><input type='hidden' id='buttonColor_" + newstatus.optionsCount + "' name='buttonColor_" + newstatus.optionsCount + "' value='" + $('#buttonColor').val() + "'><input type='hidden' id='textColor_" + newstatus.optionsCount + "' name='textColor_" + newstatus.optionsCount + "' value='" + $('#textColor').val() + "'><input type='hidden' id='detailType_" + newstatus.optionsCount + "' name='detailType_" + newstatus.optionsCount + "' value='" + $('#detailType').val() + "'></input><input type='hidden' id='noteType_" + newstatus.optionsCount + "' name='noteType_" + newstatus.optionsCount + "' value='" + $('#noteType').val() + "'></input><input type='hidden' id='requireGps_" + newstatus.optionsCount + "' name='requireGps_" + newstatus.optionsCount + "' value='" + $('#requireGps').val() + "'></input></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='btn btn-xs btn-danger' data-original-title='Remove this option'>Remove</a></td></tr>");
                }
            }
            newstatus.addOption = addOption;
            function isNumber(evt) {
                evt = (evt) ? evt : window.event;
                var charCode = (evt.which) ? evt.which : evt.keyCode;
                if (charCode > 31 && (charCode < 48 || charCode > 57)) {
                    return false;
                }
                return true;
            }
            newstatus.isNumber = isNumber;
        })(newstatus = statuses.newstatus || (statuses.newstatus = {}));
    })(statuses = resgrid.statuses || (resgrid.statuses = {}));
})(resgrid || (resgrid = {}));
