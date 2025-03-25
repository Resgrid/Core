var textToCallDataSource;
var resgrid;
(function (resgrid) {
    var department;
    (function (department) {
        var textsettings;
        (function (textsettings) {
            $(document).ready(function () {
                textToCallDataSource = new kendo.data.DataSource({
                    transport: {
                        read: {
                            url: resgrid.absoluteBaseUrl + '/User/Department/GetCallTextTypes',
                            dataType: "json"
                        }
                    },
                    pageSize: 12
                });
                $("#textTypesPager").kendoPager({
                    dataSource: textToCallDataSource
                });
                $("#EnableTextCommand").change(function () {
                    var val = $("#EnableTextCommand").val();
                    if (val)
                        $('#betaWarning').show();
                    else
                        $('#betaWarning').hide();
                });
                function onTextDataBound() {
                    SetSelectedByIDText($("#TextCallType").val());
                }
                ;
                function onTextChange() {
                    var data = textToCallDataSource.view(), selected = $.map(this.select(), function (item) {
                        return data[$(item).index()].Id;
                    });
                    $("#TextCallType").val(selected);
                }
                ;
                function SetSelectedByIDText(id) {
                    var data = textToCallDataSource.view();
                    var listView = $("#textTypesListView").data("kendoListView");
                    var children = listView.element.children();
                    var index = -1;
                    for (var x = 0; x < data.length; x++) {
                        if (data[x].Id == id) {
                            index = x;
                        }
                        ;
                    }
                    ;
                    if (index != -1) {
                        listView.select(children[index]);
                    }
                }
                ;
                $("#textTypesListView").kendoListView({
                    dataSource: textToCallDataSource,
                    selectable: "single",
                    dataBound: onTextDataBound,
                    change: onTextChange,
                    template: kendo.template($("#textTemplate").html())
                });
            });
            $('#searchNumbers').click(function () {
                if ($('#country').val()) {
                    kendo.ui.progress($("#phoneNumberTableBody"), true);
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
                            kendo.ui.progress($("#personnelGrid"), false);
                            $('#phoneNumberTableBody').html(tableHtml);
                        });
                }
                else {
                    kendo.ui.progress($("#personnelGrid"), false);
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
