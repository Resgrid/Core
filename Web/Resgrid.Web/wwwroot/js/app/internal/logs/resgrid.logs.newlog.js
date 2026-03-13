
var resgrid;
(function (resgrid) {
    var logs;
    (function (logs) {
        var newlog;
        (function (newlog) {
            let quill1;
            let quill2;
            let quill3;
            let quill4;

            $(document).ready(function () {
                newlog.validationFilter = ".training-validation, .work-validation, .meeting-validation, .coroner-validation, .callback-validation";


                //$('#newLogForm').on('submit', function (e) {
                //    e.preventDefault();
                //    $('#Log_Narrative').val(quill1.root.innerHTML);
                //    $('#Call_NatureOfCall').val(quill2.root.innerHTML);
                //    $('#Log_Cause').val(quill3.root.innerHTML);
                //    $('#Log_InitialReport').val(quill4.root.innerHTML);

                //    return true;
                //});

                supressValidation();
                var dtpOpts = { format: 'm/d/Y H:i', step: 1 };
                $('#Call_LoggedOn').datetimepicker(dtpOpts);
                $("#Call_LoggedOn").keypress(function (e) { e.preventDefault(); });
                $('#Log_StartedOn').datetimepicker(dtpOpts);
                $("#Log_StartedOn").keypress(function (e) { e.preventDefault(); });
                $('#Log_EndedOn').datetimepicker(dtpOpts);
                $("#Log_EndedOn").keypress(function (e) { e.preventDefault(); });
                $('#meetingStartedOn').datetimepicker(dtpOpts);
                $("#meetingStartedOn").keypress(function (e) { e.preventDefault(); });
                $('#meetingEndedOn').datetimepicker(dtpOpts);
                $("#meetingEndedOn").keypress(function (e) { e.preventDefault(); });
                $('#workStartTime').datetimepicker(dtpOpts);
                $("#workStartTime").keypress(function (e) { e.preventDefault(); });
                $('#workEndTime').datetimepicker(dtpOpts);
                $("#workEndTime").keypress(function (e) { e.preventDefault(); });
                $('#coronerDate').datetimepicker({ timepicker: false, format: 'm/d/Y' });
                $("#coronerDate").keypress(function (e) { e.preventDefault(); return false; });

                quill1 = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                quill2 = new Quill('#editor-container2', {
                    placeholder: '',
                    theme: 'snow'
                });

                quill3 = new Quill('#editor-container3', {
                    placeholder: '',
                    theme: 'snow'
                });

                quill4 = new Quill('#editor-container4', {
                    placeholder: '',
                    theme: 'snow'
                });



                // File upload is a standard multi-file input — no Kendo needed
                $('.sl2').select2().on("change", function (e) {
                    switchLogs(e.val);
                });
                // Bootstrap modal wrappers replacing kendoWindow
                $('#callsWindow').on('show.bs.modal', function () {
                    var $b = $(this).find('.modal-body');
                    if ($.trim($b.html()) === '') { $b.load(resgrid.absoluteBaseUrl + '/User/Dispatch/SmallCallGrid'); }
                });
                $('#unitsWindow').on('show.bs.modal', function () {
                    var $b = $(this).find('.modal-body');
                    if ($.trim($b.html()) === '') { $b.load(resgrid.absoluteBaseUrl + '/User/Units/SmallUnitsGrid'); }
                });
                newlog.wndCalls = { center: function() { return this; }, open: function() { $('#callsWindow').modal('show'); }, close: function() { $('#callsWindow').modal('hide'); } };
                newlog.wndUnits = { center: function() { return this; }, open: function() { $('#unitsWindow').modal('show'); }, close: function() { $('#unitsWindow').modal('hide'); } };
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
                                $("#CallbackCallName").val(data.Name);
                                $("#Call_NatureOfCall").val(data.Nature);
                                quill2.root.innerHTML = data.Nature;
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
                $("#nonUnitPersonnel").select2({
                    placeholder: "Select Personnel...",
                    allowClear: true,
                    multiple: true,
                    ajax: {
                        url: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGridWithFilter',
                        dataType: 'json',
                        processResults: function (data) {
                            return { results: $.map(data, function (u) { return { id: u.UserId, text: u.Name }; }) };
                        }
                    }
                });
                $.ajax({
                    url: resgrid.absoluteBaseUrl + '/User/Logs/GetNonUnitPersonnelForLog?logId=' + $('#Log_LogId').val(),
                    contentType: 'application/json',
                    type: 'GET'
                }).done(function (data) {
                    if (data) {
                        data.forEach(function (u) {
                            $("#nonUnitPersonnel").append(new Option(u.Name, u.UserId, true, true));
                        });
                        $("#nonUnitPersonnel").trigger('change');
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
                        $('#coronerInformation').hide();
                        $('#callbackInformation').hide();
                        newlog.validationFilter = ".training-validation, .work-validation, .meeting-validation, .coroner-validation, .callback-validation";
                    }
                    else if (value === "Training") {
                        $('#callLogInformation').hide();
                        $('#workInformation').hide();
                        $('#trainingInformation').show();
                        $('.call-related').hide();
                        $('#unitsList').show();
                        $('#meetingInformation').hide();
                        $('.coroner-related').hide();
                        $('#coronerInformation').hide();
                        $('#callbackInformation').hide();
                        newlog.validationFilter = ".call-validation, .work-validation, .meeting-validation, .coroner-validation, .callback-validation";
                    }
                    else if (value === "Work") {
                        $('#callLogInformation').hide();
                        $('#trainingInformation').hide();
                        $('#unitsList').hide();
                        $('#workInformation').show();
                        $('.call-related').hide();
                        $('#meetingInformation').hide();
                        $('.coroner-related').hide();
                        $('#coronerInformation').hide();
                        $('#callbackInformation').hide();
                        newlog.validationFilter = ".call-validation, .training-validation, .meeting-validation, .coroner-validation, .callback-validation";
                    }
                    else if (value === "Meeting") {
                        $('#callLogInformation').hide();
                        $('#trainingInformation').hide();
                        $('#unitsList').hide();
                        $('#workInformation').hide();
                        $('.call-related').hide();
                        $('#meetingInformation').show();
                        $('.coroner-related').hide();
                        $('#coronerInformation').hide();
                        $('#callbackInformation').hide();
                        newlog.validationFilter = ".call-validation, .training-validation, .work-validation, .coroner-validation, .callback-validation";
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
                        newlog.validationFilter = ".call-validation, .training-validation, .work-validation, .meeting-validation, .callback-validation";
                    }
                    else if (value === "Callback") {
                        $('#callLogInformation').hide();
                        $('#trainingInformation').hide();
                        $('#unitsList').hide();
                        $('#workInformation').hide();
                        $('.call-related').hide();
                        $('.coroner-related').hide();
                        $('#meetingInformation').hide();
                        $('#coronerInformation').hide();
                        $('#coronerInformation').hide();
                        $('#callbackInformation').show();
                        newlog.validationFilter = ".call-validation, .training-validation, .work-validation, .meeting-validation, .coroner-validation";
                    }
                }
                supressValidation();
            }
            function supressValidation() {
                let settngs = $.data($('#newLogForm')[0], 'validator').settings;
                settngs.ignore = newlog.validationFilter;
            }
            newlog.supressValidation = supressValidation;

            function onNewLogSubmit() {
                $('#Log_Narrative').val(quill1.root.innerHTML);
                $('#Call_NatureOfCall').val(quill2.root.innerHTML);
                $('#Log_Cause').val(quill3.root.innerHTML);
                $('#Log_InitialReport').val(quill4.root.innerHTML);

                return true;
            }
            newlog.onNewLogSubmit = onNewLogSubmit;

        })(newlog = logs.newlog || (logs.newlog = {}));
    })(logs = resgrid.logs || (resgrid.logs = {}));
})(resgrid || (resgrid = {}));
