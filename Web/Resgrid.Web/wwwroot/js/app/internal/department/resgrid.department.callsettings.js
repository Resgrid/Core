var resgrid;
(function (resgrid) {
    var department;
    (function (department) {
        var callsettings;
        (function (callsettings) {
            $(document).ready(function () {
                // Load call email types and render as a selectable list with preview images
                $.getJSON(resgrid.absoluteBaseUrl + '/User/Department/GetCallEmailTypes', function (data) {
                    var $list = $("#listView").empty().addClass('call-email-type-list');
                    $.each(data, function (i, item) {
                        var checked = item.Id == $("#CallType").val() ? 'checked' : '';
                        var imgSrc = resgrid.absoluteBaseUrl + '/images/CallEmails/' + item.Code + '.png';
                        $list.append(
                            '<div class="list-group-item call-email-type-item" style="cursor:pointer">' +
                            '<label class="call-email-type-label">' +
                            '<input type="radio" name="callTypeItem" value="' + item.Id + '" ' + checked + '> ' +
                            '<img class="call-email-type-img" src="' + imgSrc + '" alt="' + item.Name + ' email format preview" ' +
                            'onerror="this.style.display=\'none\'" /> ' +
                            '<span class="call-email-type-name">' + item.Name + '</span>' +
                            '</label>' +
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
