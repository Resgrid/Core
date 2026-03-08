var resgrid;
(function (resgrid) {
    var department;
    (function (department) {
        var recipientsgrid;
        (function (recipientsgrid) {
            $(document).ready(function () {
                // Load recipients and render as grouped list
                $.getJSON(resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid', function (data) {
                    var $grid = $("#recipientsGroupsGrid").empty();
                    // Group by Type
                    var groups = {};
                    $.each(data, function (i, item) {
                        if (!groups[item.Type]) groups[item.Type] = [];
                        groups[item.Type].push(item);
                    });
                    $.each(groups, function (type, items) {
                        $grid.append('<div class="list-group-header"><strong>' + type + '</strong></div>');
                        $.each(items, function (j, item) {
                            $grid.append(
                                '<a href="#" class="list-group-item recipient-item" data-id="' + item.Id + '" data-name="' + item.Name + '" data-type="' + item.Type + '">' +
                                item.Name + '</a>'
                            );
                        });
                    });
                    $grid.on('click', '.recipient-item', function (e) {
                        e.preventDefault();
                        resgrid.department.recipientsgrid.addRecipient({ data: $(this).data() });
                    });
                });
            });
            function addRecipient(e) { }
            recipientsgrid.addRecipient = addRecipient;
            recipientsgrid.selectRecipientButton = "selectRecipient";
        })(recipientsgrid = department.recipientsgrid || (department.recipientsgrid = {}));
    })(department = resgrid.department || (resgrid.department = {}));
})(resgrid || (resgrid = {}));
