
var resgrid;
(function (resgrid) {
    var logs;
    (function (logs) {
        var newlog;
        (function (newlog) {
            $(document).ready(function () {
                newlog.validationFilter = ".training-validation, .work-validation, .meeting-validation, .coroner-validation";
                supressValidation();
                $('#Call_LoggedOn').kendoDateTimePicker({
                    interval: 1
                });
                $("#Call_LoggedOn").keypress(function (e) {
                    e.preventDefault();
                });
                $('#Log_StartedOn').kendoDateTimePicker({
                    interval: 1
                });
                $("#Log_StartedOn").keypress(function (e) {
                    e.preventDefault();
                });
                $('#Log_EndedOn').kendoDateTimePicker({
                    interval: 1
                });
                $("#Log_EndedOn").keypress(function (e) {
                    e.preventDefault();
                });
                $('#meetingStartedOn').kendoDateTimePicker({
                    interval: 1
                });
                $("#meetingStartedOn").keypress(function (e) {
                    e.preventDefault();
                });
                $('#meetingEndedOn').kendoDateTimePicker({
                    interval: 1
                });
                $("#meetingEndedOn").keypress(function (e) {
                    e.preventDefault();
                });
                $('#workStartTime').kendoDateTimePicker({
                    interval: 1
                });
                $("#workStartTime").keypress(function (e) {
                    e.preventDefault();
                });
                $('#workEndTime').kendoDateTimePicker({
                    interval: 1
                });
                $("#workEndTime").keypress(function (e) {
                    e.preventDefault();
                });
                $('#coronerDate').kendoDatePicker({});
                $("#coronerDate").keypress(function (e) {
                    e.preventDefault();
                    return false;
                });
                $("#Log_Narrative").kendoEditor();
                $("#NatureOfCall").kendoEditor();
                $("#Log_Cause").kendoEditor();
                $("#Log_InitialReport").kendoEditor();
                $("#files").kendoUpload({
                    multiple: true,
                    batch: true,
                    localization: {
                        select: "Select File"
                    }
                });
                $('.sl2').select2().on("change", function (e) {
                    switchLogs(e.val);
                });
                newlog.wndCalls = $("#callsWindow")
                    .kendoWindow({
                    title: "Select Call",
                    modal: true,
                    visible: false,
                    resizable: false,
                    content: '/User/Dispatch/SmallCallGrid',
                    width: 750,
                    height: 465
                }).data("kendoWindow");
                newlog.wndUnits = $("#unitsWindow")
                    .kendoWindow({
                    title: "Select Unit",
                    modal: true,
                    visible: false,
                    resizable: false,
                    content: '/User/Units/SmallUnitsGrid',
                    width: 750,
                    height: 465
                }).data("kendoWindow");
                switchLogs($('#LogType').select2('data').text);
                $('.callsWindow').on('selectCall', function (e, data) {
                    newlog.wndCalls.close();
                    if (data.CallId) {
                        $("#CallId").val(data.CallId);
                        $.ajax({
                            url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCallById?callId=' + data.CallId,
                            contentType: 'application/json; charset=utf-8',
                            type: 'GET'
                        }).done(function (data) {
                            if (data) {
                                $("#Call_Name").val(data.Name);
                                var editor = $("#NatureOfCall").data("kendoEditor");
                                editor.value(data.Nature);
                                $("#Call_Address").val(data.Address);
                                $("#CallPriority").val(data.Priority);

                                if (data.Type) {
                                    $("#Call_Type").val(data.Type);
                                }

                                $("#Call_LoggedOn").val(moment(data.DispatchTime).format('YYYY/MM/DD hh:mm A'));
                            }
                        });
                    }
                });
                $('.unitsWindow').on('selectUnit', function (e, data) {
                    newlog.wndUnits.close();
                    if (data.UnitId) {
                        var callId = $("#CallId").val();
                        if ($('#unit_' + data.UnitId).length <= 0) {
                            if (callId && callId > 0) {
                                $.ajax({
                                    url: resgrid.absoluteBaseUrl + '/User/Logs/CreateUnitHtmlBlock',
                                    contentType: 'application/json; charset=utf-8',
                                    type: 'GET',
                                    data: {
                                        unitId: data.UnitId,
                                        unitName: data.UnitName
                                    }
                                }).done(function (data) {
                                    if (data) {
                                        $("#unitsContainer").append(data);
                                    }
                                });
                            }
                            else {
                                $.ajax({
                                    url: resgrid.absoluteBaseUrl + '/User/Logs/CreateUnitHtmlBlock',
                                    contentType: 'application/json; charset=utf-8',
                                    type: 'GET',
                                    data: {
                                        unitId: data.UnitId,
                                        unitName: data.UnitName
                                    }
                                }).done(function (data) {
                                    if (data) {
                                        $("#unitsContainer").append(data);
                                    }
                                });
                            }
                        }
                        else {
                            resgrid.common.notifications.showError('Unit Already Exists', 'Unit already exists in log, cannot add again.');
                        }
                    }
                });
                $("#nonUnitPersonnel").kendoMultiSelect({
                    placeholder: "Select Personnel...",
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
                    url: resgrid.absoluteBaseUrl + '/User/Logs/GetNonUnitPersonnelForLog?logId=' + $('#Log_LogId').val(),
                    contentType: 'application/json',
                    type: 'GET'
                }).done(function (data) {
                    if (data) {
                        var multiSelect = $("#nonUnitPersonnel").data("kendoMultiSelect");
                        var valuesToAdd = [];
                        for (var i = 0; i < data.length; i++) {
                            valuesToAdd.push(data[i].UserId);
                        }
                        multiSelect.value(valuesToAdd);
                    }
                });
            });
            function newCall(e) {
                $("#newCallOption").removeClass('active');
                $("#selectCallOption").removeClass('active');
                $("#newCallOption").addClass('active');
                $("#CallId").val('0');
            }
            newlog.newCall = newCall;
            function selectCall(e) {
                $("#newCallOption").removeClass('active');
                $("#selectCallOption").removeClass('active');
                $("#selectCallOption").addClass('active');
                newlog.wndCalls.center().open();
            }
            newlog.selectCall = selectCall;
            function showUnits() {
                newlog.wndUnits.center().open();
            }
            newlog.showUnits = showUnits;
            function removeUnit(unitId) {
                $('#unit_' + unitId).remove();
            }
            newlog.removeUnit = removeUnit;
            function switchLogs(value) {
                value = $('#LogType').val();
                if (value) {
                    if (value === "Run") {
                        $('#callLogInformation').show();
                        $('.call-related').show();
                        $('#workInformation').hide();
                        $('#trainingInformation').hide();
                        $('#unitsList').show();
                        $('#meetingInformation').hide();
                        $('.coroner-related').hide();
                        newlog.validationFilter = ".training-validation, .work-validation, .meeting-validation, .coroner-validation";
                    }
                    else if (value === "Training") {
                        $('#callLogInformation').hide();
                        $('#workInformation').hide();
                        $('#trainingInformation').show();
                        $('.call-related').hide();
                        $('#unitsList').show();
                        $('#meetingInformation').hide();
                        $('.coroner-related').hide();
                        newlog.validationFilter = ".call-validation, .work-validation, .meeting-validation, .coroner-validation";
                    }
                    else if (value === "Work") {
                        $('#callLogInformation').hide();
                        $('#trainingInformation').hide();
                        $('#unitsList').hide();
                        $('#workInformation').show();
                        $('.call-related').hide();
                        $('#meetingInformation').hide();
                        $('.coroner-related').hide();
                        newlog.validationFilter = ".call-validation, .training-validation, .meeting-validation, .coroner-validation";
                    }
                    else if (value === "Meeting") {
                        $('#callLogInformation').hide();
                        $('#trainingInformation').hide();
                        $('#unitsList').hide();
                        $('#workInformation').hide();
                        $('.call-related').hide();
                        $('#meetingInformation').show();
                        $('.coroner-related').hide();
                        newlog.validationFilter = ".call-validation, .training-validation, .work-validation, .coroner-validation";
                    }
                    else if (value === "Coroner") {
                        $('#callLogInformation').hide();
                        $('#trainingInformation').hide();
                        $('#unitsList').show();
                        $('#workInformation').hide();
                        $('.call-related').hide();
                        $('.coroner-related').show();
                        $('#meetingInformation').hide();
                        $('#coronerInformation').show();
                        newlog.validationFilter = ".call-validation, .training-validation, .work-validation, .meeting-validation";
                    }
                }
                supressValidation();
            }
            function supressValidation() {
                var settngs = $.data($('#newLogForm')[0], 'validator').settings;
                settngs.ignore = newlog.validationFilter;
            }
            newlog.supressValidation = supressValidation;
        })(newlog = logs.newlog || (logs.newlog = {}));
    })(logs = resgrid.logs || (resgrid.logs = {}));
})(resgrid || (resgrid = {}));
