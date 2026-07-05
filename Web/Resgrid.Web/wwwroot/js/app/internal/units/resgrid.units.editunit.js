
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
            function buildPersonnelRoleOptions(selectedId) {
                var opts = "<option value=''>-- None --</option>";
                var roles = resgrid.units.editunit.personnelRoles || [];
                for (var i = 0; i < roles.length; i++) {
                    var sel = (selectedId != null && String(selectedId) === String(roles[i].id)) ? " selected" : "";
                    var name = $('<div>').text(roles[i].name).html();
                    opts += "<option value='" + roles[i].id + "'" + sel + ">" + name + "</option>";
                }
                return opts;
            }
            editunit.buildPersonnelRoleOptions = buildPersonnelRoleOptions;
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
            editunit.addRole = addRole;
            function removeRole() {
                $(this).closest('tr').remove();
            }
            editunit.removeRole = removeRole;
        })(editunit = units.editunit || (units.editunit = {}));
    })(units = resgrid.units || (resgrid.units = {}));
})(resgrid || (resgrid = {}));
