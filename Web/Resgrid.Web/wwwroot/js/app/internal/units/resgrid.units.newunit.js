
var resgrid;
(function (resgrid) {
    var units;
    (function (units) {
        var newunit;
        (function (newunit) {
            $(document).ready(function () {
                resgrid.units.roleCounter.seed(0);
                $('select').select2();
                $(".removeRole").click(function () {
                    $(this).closest('tr').remove();
                });
            });
            function buildPersonnelRoleOptions(selectedId) {
                var opts = "<option value=''>-- None --</option>";
                var roles = resgrid.units.newunit.personnelRoles || [];
                for (var i = 0; i < roles.length; i++) {
                    var sel = (selectedId != null && String(selectedId) === String(roles[i].id)) ? " selected" : "";
                    var name = $('<div>').text(roles[i].name).html();
                    opts += "<option value='" + roles[i].id + "'" + sel + ">" + name + "</option>";
                }
                return opts;
            }
            newunit.buildPersonnelRoleOptions = buildPersonnelRoleOptions;
            function addRole() {
                var count = resgrid.units.roleCounter.next();
                var options = buildPersonnelRoleOptions(null);
                $('#unitRoles tbody').append(
                    "<tr>" +
                    "<td><input id='unitRole_" + count + "' name='unitRole_" + count + "' style='width:100%;'></td>" +
                    "<td><select id='unitRolePersonnelRole_" + count + "' name='unitRolePersonnelRole_" + count + "' style='width:100%;'>" + options + "</select></td>" +
                    "<td style='text-align:center;'><input type='checkbox' id='unitRoleRequired_" + count + "' name='unitRoleRequired_" + count + "'></td>" +
                    "<td style='text-align:center;'><a onclick='$(this).closest(\"tr\").remove();' class='btn btn-xs btn-danger' data-original-title='Remove this role'>Remove</a></td>" +
                    "</tr>");
            }
            newunit.addRole = addRole;
            function removeRole() {
                $(this).closest('tr').remove();
            }
            newunit.removeRole = removeRole;
        })(newunit = units.newunit || (units.newunit = {}));
    })(units = resgrid.units || (resgrid.units = {}));
})(resgrid || (resgrid = {}));
