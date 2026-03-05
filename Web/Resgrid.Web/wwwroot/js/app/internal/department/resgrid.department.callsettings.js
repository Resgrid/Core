var resgrid;
(function (resgrid) {
    var department;
    (function (department) {
        var callsettings;
        (function (callsettings) {
            $(document).ready(function () {
                // Load call email types and render as a selectable list
                $.getJSON(resgrid.absoluteBaseUrl + '/User/Department/GetCallEmailTypes', function (data) {
                    var $list = $("#listView").empty();
                    $.each(data, function (i, item) {
                        var checked = item.Id == $("#CallType").val() ? 'checked' : '';
                        $list.append(
                            '<div class="list-group-item" style="cursor:pointer">' +
                            '<label><input type="radio" name="callTypeItem" value="' + item.Id + '" ' + checked + '> ' + item.Name + '</label>' +
                            '</div>'
                        );
                    });
                    $list.on('change', 'input[type=radio]', function () {
                        $("#CallType").val($(this).val());
                        $list.find('.list-group-item').removeClass('active');
                        $(this).closest('.list-group-item').addClass('active');
                    });
                    // Highlight currently selected
                    $list.find('input[type=radio]:checked').closest('.list-group-item').addClass('active');
                });
            });
        })(callsettings = department.callsettings || (department.callsettings = {}));
    })(department = resgrid.department || (resgrid.department = {}));
})(resgrid || (resgrid = {}));
