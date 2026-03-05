
var resgrid;
(function (resgrid) {
    var shifts;
    (function (shifts) {
        var shiftStaffing;
        (function (shiftStaffing) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Shifts - Shift Staffing');
                $('.sl2').select2();
                $("#shiftDayPicker").keypress(function (e) {
                    e.preventDefault();
                });
                $('#ShiftId').on("change", function (e) {
                    $.ajax({
                        url: resgrid.absoluteBaseUrl + '/User/Shifts/GetShiftDays?shiftId=' + e.val,
                        contentType: 'application/json; charset=utf-8',
                        type: 'GET'
                    }).done(function (data) {
                        resgrid.shifts.shiftStaffing.shiftDays = data;
                        resgrid.shifts.shiftStaffing.initDatePicker();
                        resgrid.shifts.shiftStaffing.createGroupInputs();
                    });
                });
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Shifts/GetShiftDays?shiftId=' + $('#ShiftId').val(),
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    resgrid.shifts.shiftStaffing.shiftDays = data;
                    resgrid.shifts.shiftStaffing.initDatePicker();
                    resgrid.shifts.shiftStaffing.createGroupInputs();
                });
            });
            function compareDates(date, dates) {
                for (var i = 0; i < dates.length; i++) {
                    var shiftDay = new Date(dates[i].Day);
                    if (shiftDay.getDate() == date.getDate() &&
                        shiftDay.getMonth() == date.getMonth() &&
                        shiftDay.getFullYear() == date.getFullYear()) {
                        return true;
                    }
                }
                return false;
            }
            shiftStaffing.compareDates = compareDates;
            function initDatePicker() {
                var now = new Date();
                resgrid.shifts.shiftStaffing.datePicker = $("#shiftDayPicker").datetimepicker({
                    timepicker: false,
                    format: 'm/d/Y',
                    minDate: now,
                    scrollMonth: false,
                    scrollInput: false,
                    onGenerate: function(ct, input) {
                        // Disable days not in shiftDays
                        $(this).find('.xdsoft_date').each(function() {
                            var $td = $(this);
                            var y = $td.data('year');
                            var m = $td.data('month'); // 0-based
                            var d = $td.data('date');
                            if (y !== undefined && m !== undefined && d !== undefined) {
                                var cellDate = new Date(y, m, d);
                                if (!resgrid.shifts.shiftStaffing.compareDates(cellDate, shiftStaffing.shiftDays)) {
                                    $td.addClass('xdsoft_disabled');
                                }
                            }
                        });
                    }
                });
            }
            shiftStaffing.initDatePicker = initDatePicker;
            function createGroupInputs() {
                var html = "";
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Shifts/GetShiftGroups?shiftId=' + $('#ShiftId').val(),
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data) {
                    var ids = new Array();

                    if (isAdmin) {
                        html = '<div class="form-group"><label class="control-label">Non - Group Personnel</label><div class="controls"><div class="col-xs-6"><select id="shiftPersonnel" name="shiftPersonnel"></select></div></div></div>';
                    }
                    for (var i = 0; i < data.length; i++) {
                        if (isAdmin || data[i].Id == groupId) {
                            var itemHtml = '<div class="form-group"><label class="control-label">' + data[i].Name + '</label><div class="controls"><div class="col-md-6">';

                            itemHtml = itemHtml + '<div class="row"><div class="col-sm-10"><select id="groupPersonnel_' + data[i].Id + '" name="groupPersonnel_' + data[i].Id + '" class="groupPersonnelSelect"></select></div></div>';
                            itemHtml = itemHtml + `<div id="groupUnits_${data[i].Id}"></div></div></div></div>`;

                            ids.push(data[i].Id);
                            html = html + itemHtml;
                        }
                    }
                    $('#groupStaffing').empty().append(html);

                    //for (var i = 0; i < ids.length; i++) {
                    //    createUnitInputs(ids[i]);
                    //}

                    if (isAdmin) {
                        $("#shiftPersonnel").select2({
                            placeholder: "Select Non-Group Personnel...",
                            allowClear: true,
                            multiple: true,
                            ajax: {
                                url: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter',
                                dataType: 'json',
                                processResults: function(data) { return { results: $.map(data, function(u) { return { id: u.UserId, text: u.Name }; }) }; }
                            }
                        });
                        $.ajax({
                            url: resgrid.absoluteBaseUrl + '/User/Shifts/GetPersonnelForShift?shiftId=' + $('#ShiftId').val() + '&groupId=0',
                            contentType: 'application/json', type: 'GET'
                        }).done(function (data) {
                            if (data) {
                                var opts = data.map(function(u) { return new Option(u.Name, u.UserId, true, true); });
                                opts.forEach(function(o) { $('#shiftPersonnel').append(o); });
                                $('#shiftPersonnel').trigger('change');
                            }
                        });
                    }
                    $('.groupPersonnelSelect').each(function (i, obj) {
                        var that = this;
                        $(that).select2({
                            placeholder: "Select Personnel for Group...",
                            allowClear: true,
                            multiple: true,
                            ajax: {
                                url: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter',
                                dataType: 'json',
                                processResults: function(data) { return { results: $.map(data, function(u) { return { id: u.UserId, text: u.Name }; }) }; }
                            }
                        });
                        var groupId = $(that).attr("name").replace('groupPersonnel_', '');
                        $.ajax({
                            url: resgrid.absoluteBaseUrl + '/User/Shifts/GetPersonnelForShift?shiftId=' + $('#ShiftId').val() + '&groupId=' + groupId,
                            contentType: 'application/json', type: 'GET'
                        }).done(function (userData) {
                            if (userData) {
                                var opts = userData.map(function(u) { return new Option(u.Name, u.UserId, true, true); });
                                opts.forEach(function(o) { $(that).append(o); });
                                $(that).trigger('change');
                            }
                        });
                    });
                });
            }
            shiftStaffing.createGroupInputs = createGroupInputs;


            function createUnitInputs(groupId) {
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsAndRolesForGroup?groupId=' + groupId,
                    contentType: 'application/json; charset=utf-8',
                    type: 'GET'
                }).done(function (data2) {
                    if (data2) {
                        $(`#groupUnits_${groupId}`).empty();

                        for (var j = 0; j < data2.length; j++) {
                            if (data2[j] && data2[j].Roles) {
                                //var unitsHtml = `<div class="col-sm-10">${data2[j].Name}</div>`;
                                var unitsHtml = `<div class="form-group"><label class="control-label">${data2[j].Name}</label><div class="controls"><div class="col-md-6">`

                                var roleIds = new Array();
                                for (var k = 0; k < data2[j].Roles.length; k++) {
                                    roleIds.push(data2[j].Roles[k].UnitRoleId);
                                    unitsHtml = unitsHtml + `<div class="row"><div class="col-sm-5">${data2[j].Roles[k].Name}</div>`;
                                    unitsHtml = unitsHtml + `<div class="col-sm-5"><select id="unitRole_${data2[j].UnitId}_${data2[j].Roles[k].UnitRoleId}" name="unitRole_${data2[j].UnitId}_${data2[j].Roles[k].RoleId}" class="unitRoleSelect"></select></div></div>`;
                                }

                                unitsHtml = unitsHtml + `</div>`;

                                if (data2[j].GroupId && data2[j].GroupId > 0) {
                                    $(`#groupUnits_${data2[j].GroupId}`).append(unitsHtml);

                                    for (var l = 0; l < roleIds.length; l++) {
                                        $(`#unitRole_${data2[j].UnitId}_${roleIds[l]}`).each(function (i, obj) {
                                            var that = this;
                                            $(that).select2({
                                                placeholder: "Select Person...",
                                                allowClear: true,
                                                ajax: {
                                                    url: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter',
                                                    dataType: 'json',
                                                    processResults: function(data) { return { results: $.map(data, function(u) { return { id: u.UserId, text: u.Name }; }) }; }
                                                }
                                            });
                                        });
                                    }
                                }
                            }
                        }
                    }
                });
            }
        })(shiftStaffing = shifts.shiftStaffing || (shifts.shiftStaffing = {}));
    })(shifts = resgrid.shifts || (resgrid.shifts = {}));
})(resgrid || (resgrid = {}));
