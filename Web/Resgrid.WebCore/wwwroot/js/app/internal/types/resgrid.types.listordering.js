
var resgrid;
(function (resgrid) {
    var types;
    (function (types) {
        var listordering;
        (function (listordering) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Department - List Ordering');
                //resgrid.types.listordering.statusCount = 0;
            });
            function addStatus() {
                var statusValue = $('#newPersonnelStatus').val();
                var statusText = $( "#newPersonnelStatus option:selected" ).text();

                $('#addStatusOrderingModal').modal('hide');
                $("#addOptionErrors").hide();
                resgrid.types.listordering.statusCount++;
                $('#personnelStatusOrdering tbody').first().append("<tr><td><input type='number' min='0' id='personnelStatus_" + resgrid.types.listordering.statusCount + "' name='personnelStatus_" + resgrid.types.listordering.statusCount + "' value='0' onkeypress='return resgrid.types.listordering.isNumber(event)'></td><td>" + statusText + "<input type='hidden' id='personnelStatusValue_" + resgrid.types.listordering.statusCount + "' name='personnelStatusValue_" + resgrid.types.listordering.statusCount + "' value='" + statusValue + "'></input></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='btn btn-xs btn-danger' data-original-title='Delete this option'>Delete</a></td></tr>");
            }
            listordering.addStatus = addStatus;
            function isNumber(evt) {
                evt = (evt) ? evt : window.event;
                var charCode = (evt.which) ? evt.which : evt.keyCode;
                if (charCode > 31 && (charCode < 48 || charCode > 57)) {
                    return false;
                }
                return true;
            }
            listordering.isNumber = isNumber;
        })(listordering = types.listordering || (types.listordering = {}));
    })(types = resgrid.types || (resgrid.types = {}));
})(resgrid || (resgrid = {}));
