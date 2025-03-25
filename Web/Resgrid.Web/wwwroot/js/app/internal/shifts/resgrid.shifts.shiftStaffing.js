
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
                resgrid.shifts.shiftStaffing.datePicker = $("#shiftDayPicker").kendoDatePicker({
                    value: new Date(),
                    disableDates: function (date) {
                        var now = new Date();
                        if (date < now)
                            return true;
                        if (date && resgrid.shifts.shiftStaffing.compareDates(date, shiftStaffing.shiftDays)) {
                            return false;
                        }
                        else {
                            return true;
                        }
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
                        $("#shiftPersonnel").kendoMultiSelect({
                            placeholder: "Select Non-Group Personnel...",
                            dataTextField: "Name",
                            dataValueField: "UserId",
                            autoBind: false,
                            dataSource: {
                                type: "json",
                                transport: {
                                    read: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter'
                                }
                            }
                        });
                        $.ajax({
                            url: resgrid.absoluteBaseUrl + '/User/Shifts/GetPersonnelForShift?shiftId=' + $('#ShiftId').val() + '&groupId=0',
                            contentType: 'application/json',
                            type: 'GET'
                        }).done(function (data) {
                            if (data) {
                                var multiSelect = $("#shiftPersonnel").data("kendoMultiSelect");
                                var valuesToAdd = [];
                                for (var i = 0; i < data.length; i++) {
                                    valuesToAdd.push(data[i].UserId);
                                }
                                multiSelect.value(valuesToAdd);
                            }
                        });
                    }
                    $('.groupPersonnelSelect').each(function (i, obj) {
                        var that = this;
                        $(that).kendoMultiSelect({
                            placeholder: "Select Personnel for Group...",
                            dataTextField: "Name",
                            dataValueField: "UserId",
                            autoBind: false,
                            dataSource: {
                                type: "json",
                                transport: {
                                    read: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter'
                                }
                            }
                        });
                        var groupId = $(that).attr("name");
                        groupId = groupId.replace('groupPersonnel_', '');
                        $.ajax({
                            url: resgrid.absoluteBaseUrl + '/User/Shifts/GetPersonnelForShift?shiftId=' + $('#ShiftId').val() + '&groupId=' + groupId,
                            contentType: 'application/json',
                            type: 'GET'
                        }).done(function (userData) {
                            if (userData) {
                                var multiSelect = $(that).data("kendoMultiSelect");
                                var valuesToAdd = [];
                                for (var i = 0; i < userData.length; i++) {
                                    valuesToAdd.push(userData[i].UserId);
                                }
                                multiSelect.value(valuesToAdd);
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
                                            $(that).kendoComboBox({
                                                placeholder: "Select Person...",
                                                dataTextField: "Name",
                                                dataValueField: "UserId",
                                                autoBind: false,
                                                dataSource: {
                                                    type: "json",
                                                    transport: {
                                                        read: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter'
                                                    }
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
