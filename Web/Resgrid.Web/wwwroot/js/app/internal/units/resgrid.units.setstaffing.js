
var resgrid;
(function (resgrid) {
    var units;
    (function (units) {
        var setstaffing;
        (function (setstaffing) {
            $(document).ready(function () {

                $("select").change(function () {
                    var selectedUserId = $(this).val();

                    if (selectedUserId) {
                        $("select").not(this).find("option[value=" + selectedUserId + "]").each(function () {
                            if ($(this).is(':selected')) {
                                swal({
                                    title: "User Already Assigned Role",
                                    text: "A user can only be assigned to one unit role. Please ensure a user is assigned to only one unit role before trying set the unit staffing.",
                                    icon: "warning",
                                    buttons: true,
                                    dangerMode: true
                                }).then((done) => {

                                });
                            }
                        });

                        //$("select").not(this).find("option[value=" + $(this).val() + "]").attr('disabled', true);
                    }
                });

                $('.selectize').selectize({
                    valueField: 'UserId',
                    labelField: 'Name',
                    searchField: 'Name',
                    options: [],
                    create: false,
                    // Keep the "Not Occupied" option (empty value) as a real choice so an assigned seat can be
                    // cleared; without this Selectize drops it and only uses its text as the placeholder.
                    allowEmptyOption: true,
                    render: {
                        option: function (item, escape) {
                            if (!item.UserId) {
                                return '<div><span class="title"><span class="name text-muted">' + escape(item.Name) + '</span></span></div>';
                            }

                            return '<div>' +
                                '<span class="title">' +
                                '<span class="name">' + escape(item.Name) + '</span>' +
                                '<span class="by">' + escape(item.GroupName) + '</span>' +
                                '</span>' +
                                '<span class="description">' + escape(item.Roles) + '</span>' +
                                '</div>';
                        }
                    }
                });
            });
        })(viewevents = units.setstaffing || (units.setstaffing = {}));
    })(units = resgrid.units || (resgrid.units = {}));
})(resgrid || (resgrid = {}));
