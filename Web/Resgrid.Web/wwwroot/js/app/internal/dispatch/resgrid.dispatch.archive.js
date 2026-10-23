var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var archive;
        (function (archive) {
            var archiveTable;
            // A call's name with its type icon (uploaded or built-in, from TypeIconUrl) in front, both escaped; sorting and
            // search keep using the plain name.
            function typeIconNameCell(name, type, row) {
                if (type !== 'display') { return name; }
                var text = $('<div/>').text(name || '').html();
                if (!row || !row.TypeIconUrl) { return text; }
                return '<img src="' + $('<div/>').text(row.TypeIconUrl).html() + '" alt="" aria-hidden="true" class="rg-type-icon" ' +
                    'style="width:20px;height:20px;object-fit:contain;vertical-align:middle;margin-right:4px;" />' + text;
            }

            function getText(key, fallback) {
                return (resgrid.dispatch && typeof resgrid.dispatch.getText === 'function')
                    ? resgrid.dispatch.getText(key, fallback)
                    : fallback;
            }
            $(document).ready(function () {
                resgrid.common.analytics.track('Dispatch Archive');

                archiveTable = $("#archiveCallsList").DataTable({
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetArchivedCallsList?year=' + $("#Year").val(),
                        dataSrc: ''
                    },
                    pageLength: 50,
                    columns: [
                        { data: 'Number', title: getText('number', 'Number'), type: 'string', render: resgrid.callNumberCell },
                        { data: 'Name', title: getText('name', 'Name'), render: typeIconNameCell },
                        { data: 'Timestamp', title: getText('timestamp', 'Timestamp') },
                        {
                            data: null, title: getText('priority', 'Priority'), orderable: false,
                            render: function (data, type, row) {
                                return '<span style="background-color:' + resgrid.escapeHtml(row.Color) + ';color:#fff;padding:2px 6px;border-radius:3px;">' + resgrid.escapeHtml(row.Priority) + '</span>';
                            }
                        },
                        {
                            data: null, title: getText('state', 'State'), orderable: false,
                            render: function (data, type, row) {
                                return '<span style="background-color:' + resgrid.escapeHtml(row.StateColor) + ';color:#fff;padding:2px 6px;border-radius:3px;">' + resgrid.escapeHtml(row.State) + '</span>';
                            }
                        },
                        {
                            data: 'CallId', title: getText('actions', 'Actions'), orderable: false,
                            render: function (data, type, row) {
                                var html = '<a class="btn btn-xs btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Dispatch/ViewCall?callId=' + data + '">' + getText('view', 'View') + '</a> ';
                                if (row.CanDeleteCall) {
                                    html += '<a class="btn btn-xs btn-danger" href="' + resgrid.absoluteBaseUrl + '/User/Dispatch/DeleteCall?callId=' + data + '">' + getText('delete', 'Delete') + '</a>';
                                }
                                return html;
                            }
                        }
                    ]
                });

                $("#Year").change(function () {
                    archiveTable.ajax.url(resgrid.absoluteBaseUrl + '/User/Dispatch/GetArchivedCallsList?year=' + $(this).val()).load();
                });
            });
        })(archive = dispatch.archive || (dispatch.archive = {}));
    })(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
