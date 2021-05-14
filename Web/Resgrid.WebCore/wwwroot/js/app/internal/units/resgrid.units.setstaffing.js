
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
                
            });
        })(viewevents = units.setstaffing || (units.setstaffing = {}));
    })(units = resgrid.units || (resgrid.units = {}));
})(resgrid || (resgrid = {}));
