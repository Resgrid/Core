var resgrid;
(function (resgrid) {
    var connect;
    (function (connect) {
        var posts;
        (function (posts) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Connect Posts');
                $("#postsList").DataTable({
                    ajax: { url: resgrid.absoluteBaseUrl + '/User/Connect/GetPostsList?departmentProfileId=' + departmentProfileId, dataSrc: '' },
                    pageLength: 50,
                    columns: [
                        { data: 'Title', title: 'Title' },
                        { data: 'CreatedOn', title: 'Created On' },
                        { data: 'ExpiresOn', title: 'Expires On' },
                        { data: 'CreatedBy', title: 'Created By' },
                        {
                            data: 'Id', title: 'Actions', orderable: false, searchable: false,
                            render: function (data) {
                                return '<a class="btn btn-xs btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Connect/ViewPost?postId=' + data + '">View</a> ' +
                                       '<a class="btn btn-xs btn-danger" href="' + resgrid.absoluteBaseUrl + '/User/Connect/DeletePost?postId=' + data + '">Delete</a>';
                            }
                        }
                    ]
                });
            });
        })(posts = connect.posts || (connect.posts = {}));
    })(connect = resgrid.connect || (resgrid.connect = {}));
})(resgrid || (resgrid = {}));
