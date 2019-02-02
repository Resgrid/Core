
var resgrid;
(function (resgrid) {
    var units;
    (function (units) {
        var newunit;
        (function (newunit) {
            $(document).ready(function () {
                count = 0;
                $('select').select2();
                $(".removeRole").click(function () {
                    $(this).closest('tr').remove();
                });
            });
            function addRole() {
                count++;
                $('#unitRoles tbody').append("<tr><td><input id='unitRole_" + count + "' name='unitRole_" + count + "' style='width:100%;'></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='btn btn-xs btn-danger' data-original-title='Remove this role'>Remove</a></td></tr>");
            }
            newunit.addRole = addRole;
            function removeRole() {
                $(this).closest('tr').remove();
            }
            newunit.removeRole = removeRole;
        })(newunit = units.newunit || (units.newunit = {}));
    })(units = resgrid.units || (resgrid.units = {}));
})(resgrid || (resgrid = {}));
