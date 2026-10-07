
var resgrid;
(function (resgrid) {
    var statuses;
    (function (statuses) {
        var newstatus;
        (function (newstatus) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Custom Statuses - New');

                // Preserve any count set by the view when the form is pre-filled from a template
                // (rows already rendered server-side); only default to 0 for a blank/fresh slate.
                if (typeof resgrid.statuses.newstatus.optionsCount === 'undefined') {
                    resgrid.statuses.newstatus.optionsCount = 0;
                }

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
            function hiddenInput(name, value) {
                return $('<input type="hidden">').attr({ id: name, name: name }).val(value);
            }
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
                    var n = newstatus.optionsCount;
                    var buttonColor = $('#buttonColor').val();
                    var textColor = $('#textColor').val();
                    var baseTypeVal = $('#baseType').length ? $('#baseType').val() : '-1';
                    var detailTypeVal = $('#detailType').length ? $('#detailType').val() : '0';
                    var noteTypeVal = $('#noteType').length ? $('#noteType').val() : '0';
                    var requireGpsVal = $('#requireGps').length && $('#requireGps').is(':checked') ? 'on' : 'false';

                    // Built with jQuery, never by concatenating markup, so a typed or pasted value cannot become HTML or
                    // break out of an attribute.
                    var row = $('<tr>');
                    row.append($('<td>').append($('<input type="number" min="0" value="0">').attr({ id: 'order_' + n, name: 'order_' + n })
                        .on('keypress', function (e) { return isNumber(e); })));
                    row.append($('<td>').text(buttonText).append(hiddenInput('buttonText_' + n, buttonText), hiddenInput('baseType_' + n, baseTypeVal)));
                    row.append($('<td>').append(
                        $('<a class="btn btn-default" role="button">').css({ color: textColor, background: buttonColor }).text(buttonText),
                        hiddenInput('buttonColor_' + n, buttonColor),
                        hiddenInput('textColor_' + n, textColor),
                        hiddenInput('detailType_' + n, detailTypeVal),
                        hiddenInput('noteType_' + n, noteTypeVal),
                        hiddenInput('requireGps_' + n, requireGpsVal)));
                    row.append($('<td style="text-align:center;">').append(
                        $('<a class="btn btn-xs btn-danger" data-original-title="Remove this option">').text('Remove')
                            .on('click', function () { $(this).closest('tr').remove(); })));
                    $('#options tbody').first().append(row);
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
