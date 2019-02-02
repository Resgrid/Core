var resgrid;
(function (resgrid) {
    var department;
    (function (department) {
        var recipientsgrid;
        (function (recipientsgrid) {
            $(document).ready(function () {
                var dataSrouce = new kendo.data.DataSource({
                    type: "json",
                    transport: {
                        read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid'
                    },
                    schema: {
                        model: {
                            fields: {
                                Id: { type: "string" },
                                Type: { type: "string" },
                                Name: { type: "string" }
                            }
                        }
                    }
                });
                $("#recipientsGroupsGrid").kendoListView({
                    dataSource: dataSrouce,
                    template: kendo.template($("#respondingOptionsTemplate").html()),
                    group: 'Type',
                    headerTemplate: "${value}",
                    fixedHeaders: true,
                    click: resgrid.department.recipientsgrid.addRecipient
                });
                $("#pager").kendoPager({
                    dataSource: dataSrouce
                });
            });
            function addRecipient(e) {
            }
            recipientsgrid.addRecipient = addRecipient;
            recipientsgrid.selectRecipientButton = "selectRecipient";
        })(recipientsgrid = department.recipientsgrid || (department.recipientsgrid = {}));
    })(department = resgrid.department || (resgrid.department = {}));
})(resgrid || (resgrid = {}));
