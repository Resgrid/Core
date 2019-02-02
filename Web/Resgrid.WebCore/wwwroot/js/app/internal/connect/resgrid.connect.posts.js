var resgrid;
(function (resgrid) {
    var connect;
    (function (connect) {
        var posts;
        (function (posts) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Connect Posts');
                $("#postsList").kendoGrid({
                    dataSource: {
                        type: "json",
                        transport: {
                            read: resgrid.absoluteBaseUrl + '/User/Connect/GetPostsList?departmentProfileId=' + departmentProfileId
                        },
                        schema: {
                            model: {
                                fields: {
                                    Id: { type: "number" },
                                    Title: { type: "string" },
                                    CreatedOn: { type: "string" },
                                    ExpiresOn: { type: "string" },
                                    CreatedBy: { type: "string" }
                                }
                            }
                        },
                        pageSize: 50
                    },
                    //height: 400,
                    filterable: true,
                    sortable: true,
                    scrollable: true,
                    pageable: {
                        refresh: true,
                        pageSizes: true,
                        buttonCount: 5
                    },
                    columns: [
                        "Title",
                        "CreatedOn",
                        "ExpiresOn",
                        "CreatedBy",
                        {
                            field: "Id",
                            title: "Actions",
                            filterable: false,
                            sortable: false,
                            width: 220,
                            template: kendo.template($("#command-template").html())
                        }
                    ]
                });
            });
        })(posts = connect.posts || (connect.posts = {}));
    })(connect = resgrid.connect || (resgrid.connect = {}));
})(resgrid || (resgrid = {}));
