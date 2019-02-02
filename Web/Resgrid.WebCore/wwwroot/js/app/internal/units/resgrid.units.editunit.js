
var resgrid;
(function (resgrid) {
    var units;
    (function (units) {
        var editunit;
        (function (editunit) {
            $(document).ready(function () {
                $('select').select2();
                $(".removeRole").click(function () {
                    $(this).closest('tr').remove();
                });
            });
            function addRole() {
                count++;
                $('#unitRoles tbody').append("<tr><td><input id='unitRole_" + count + "' name='unitRole_" + count + "' style='width:100%;'></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='btn btn-xs btn-danger' data-original-title='Remove this role'>Remove</a></td></tr>");
            }
            editunit.addRole = addRole;
            function removeRole() {
                $(this).closest('tr').remove();
            }
            editunit.removeRole = removeRole;
        })(editunit = units.editunit || (units.editunit = {}));
    })(units = resgrid.units || (resgrid.units = {}));
})(resgrid || (resgrid = {}));
