var resgrid;
(function (resgrid) {
    var department;
    (function (department) {
        var callsettings;
        (function (callsettings) {
            $(document).ready(function () {
                var dataSource = new kendo.data.DataSource({
                    transport: {
                        read: {
                            url: resgrid.absoluteBaseUrl + '/User/Department/GetCallEmailTypes',
                            dataType: "json"
                        }
                    },
                    pageSize: 12
                });
                $("#pager").kendoPager({
                    dataSource: dataSource
                });
                $("#listView").kendoListView({
                    dataSource: dataSource,
                    selectable: "single",
                    dataBound: onDataBound,
                    change: onChange,
                    template: kendo.template($("#template").html())
                });
                function onDataBound() {
                    SetSelectedByID($("#CallType").val());
                }
                ;
                function onChange() {
                    var data = dataSource.view(), selected = $.map(this.select(), function (item) {
                        return data[$(item).index()].Id;
                    });
                    $("#CallType").val(selected);
                }
                ;
                function SetSelectedByID(id) {
                    var data = dataSource.view();
                    var listView = $("#listView").data("kendoListView");
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
            });
        })(callsettings = department.callsettings || (department.callsettings = {}));
    })(department = resgrid.department || (resgrid.department = {}));
})(resgrid || (resgrid = {}));
