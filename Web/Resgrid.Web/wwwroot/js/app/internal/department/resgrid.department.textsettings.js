var textToCallDataSource;
var resgrid;
(function (resgrid) {
    var department;
    (function (department) {
        var textsettings;
        (function (textsettings) {
            $(document).ready(function () {
                // Load call text types and render as a selectable list
                $.getJSON(resgrid.absoluteBaseUrl + '/User/Department/GetCallTextTypes', function (data) {
                    var $list = $("#textTypesListView").empty();
                    $.each(data, function (i, item) {
                        var checked = item.Id == $("#TextCallType").val() ? 'checked' : '';
                        $list.append(
                            '<div class="list-group-item" style="cursor:pointer">' +
                            '<label><input type="radio" name="textCallTypeItem" value="' + item.Id + '" ' + checked + '> ' + item.Name + '</label>' +
                            '</div>'
                        );
                    });
                    $list.on('change', 'input[type=radio]', function () {
                        $("#TextCallType").val($(this).val());
                        $list.find('.list-group-item').removeClass('active');
                        $(this).closest('.list-group-item').addClass('active');
                    });
                    $list.find('input[type=radio]:checked').closest('.list-group-item').addClass('active');
                });

                $("#EnableTextCommand").change(function () {
                    var val = $("#EnableTextCommand").val();
                    if (val)
                        $('#betaWarning').show();
                    else
                        $('#betaWarning').hide();
                });
            });
            $('#searchNumbers').click(function () {
                if ($('#country').val()) {
                    resgrid.showProgress($("#phoneNumberTableBody"), true);
                    if ($('#country').val())
                        $.ajax({
                            url: resgrid.absoluteBaseUrl + '/User/Department/GetAvailableNumbers?country=' + $('#country').val() + "&areaCode=" + $('#areaCode').val(),
                            contentType: 'application/json; charset=utf-8',
                            type: 'GET'
                        }).done(function (results) {
                            $('#phoneNumberTableBody').empty();
                            var tableHtml;
                            $.each(results, function (index, value) {
                                var tr = '<tr>';
                                tr += '<td>' + value.msisdn + '</td>';
                                tr += '<td><a class="btn btn-xs btn-primary" href="/User/Department/ProvisionNumber?msisdn=' + value.msisdn + '&country=' + $('#country').val() + '&areaCode=' + $('#areaCode').val() + '">Select This Number</a></td>';
                                tr += '</tr>';
                                tableHtml += tr;
                            });
                            resgrid.showProgress($("#personnelGrid"), false);
                            $('#phoneNumberTableBody').html(tableHtml);
                        });
                }
                else {
                    resgrid.showProgress($("#personnelGrid"), false);
                    $('#phoneNumberTableBody').empty();
                }
            });
            //$('#country').on('change', function () {
            //	if ($('#country').val()) {
            //		if ($('#country').val() === "AU") {
            //			$('#areaCode').val("");
            //			$('#areaCode').prop('disabled', true);
            //		} else {
            //			$('#areaCode').prop('disabled', false);
            //		}
            //	}
            //});
        })(textsettings = department.textsettings || (department.textsettings = {}));
    })(department = resgrid.department || (resgrid.department = {}));
})(resgrid || (resgrid = {}));
