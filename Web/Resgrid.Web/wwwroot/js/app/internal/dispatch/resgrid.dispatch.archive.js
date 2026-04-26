var resgrid;
(function (resgrid) {
    var dispatch;
    (function (dispatch) {
        var archive;
        (function (archive) {
            var archiveTable;
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
                        { data: 'Number', title: getText('number', 'Number') },
                        { data: 'Name', title: getText('name', 'Name') },
                        { data: 'Timestamp', title: getText('timestamp', 'Timestamp') },
                        {
                            data: null, title: getText('priority', 'Priority'), orderable: false,
                            render: function (data, type, row) {
                                return '<span style="background-color:' + row.Color + ';color:#fff;padding:2px 6px;border-radius:3px;">' + row.Priority + '</span>';
                            }
                        },
                        {
                            data: null, title: getText('state', 'State'), orderable: false,
                            render: function (data, type, row) {
                                return '<span style="background-color:' + row.StateColor + ';color:#fff;padding:2px 6px;border-radius:3px;">' + row.State + '</span>';
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
