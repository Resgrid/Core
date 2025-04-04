
var resgrid;
(function (resgrid) {
    var contacts;
    (function (contacts) {
        var index;
        (function (index) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Contacts List');

                $('.table').DataTable();
                $('#tree').bstreeview({ data: treeData });
                $('#TreeGroup_-1').css("font-weight", "bold");

                $(document).on('click', '.list-group-item', function (e) {
                    if (e) {
                        $('.contactsTabPannel').each(function (i, el) {
                            $(el).hide();
                        });

                        if (e.target) {
                            $('.list-group-item').each(function (i, el) {
                                if (el.textContent === e.target.textContent)
                                    $(el).css("font-weight", "bold");
                                else
                                    $(el).css("font-weight", "normal");
                            });

                            $("#contactsTab" + e.target.id.replace('TreeGroup_', '')).show();

                            $.fn.dataTable
                                .tables({ visible: true, api: true })
                                .columns.adjust().draw();
                        }
                    }
                });
            });
        })(index = units.index || (units.index = {}));
    })(units = resgrid.contacts || (resgrid.contacts = {}));
})(resgrid || (resgrid = {}));
