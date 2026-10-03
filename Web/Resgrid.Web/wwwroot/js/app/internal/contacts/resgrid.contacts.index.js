
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

                        // Match the clicked node by id, not text: two categories can share a name.
                        var node = this;
                        if (node && node.id) {
                            $('.list-group-item').each(function (i, el) {
                                if (el.id === node.id)
                                    $(el).css("font-weight", "bold");
                                else
                                    $(el).css("font-weight", "normal");
                            });

                            $("#contactsTab" + node.id.replace('TreeGroup_', '')).show();

                            $.fn.dataTable
                                .tables({ visible: true, api: true })
                                .columns.adjust().draw();
                        }
                    }
                });
            });
        })(index = contacts.index || (contacts.index = {}));
    })(contacts = resgrid.contacts || (resgrid.contacts = {}));
})(resgrid || (resgrid = {}));
