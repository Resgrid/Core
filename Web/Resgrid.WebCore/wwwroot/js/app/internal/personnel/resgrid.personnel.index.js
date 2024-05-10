
var resgrid;
(function (resgrid) {
    var personnel;
    (function (personnel) {
        var index;
        (function (index) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Personnel List');

                $('.table').DataTable();
                $('#tree').bstreeview({ data: treeData });
                $('.selectPersonCheckbox').click(evaluate);
                $('#TreeGroup_-1').css("font-weight", "bold");

                $('.checkAllPersonnel').on('click', function (e) {
                    let groupType = $(this).val();
                    let checked = this.checked;

                    $('.table').find(':checkbox').each(function (i, el) {
                        let elementGroupType = $(el).attr('data-grouptype')

                        if (groupType === elementGroupType) {
                            $(el).prop('checked', checked);
                        }

                        evaluate();
                    });
                });

                // Set Status for 1 person Modal
                $("#setPersonStatusModal").on('shown.bs.modal', function (e) {
                    let triggerLink = $(e.relatedTarget);
                    let id = triggerLink.data("id");
                    $('#setPersonStatusUserId').val(id);
                    let customStateType = triggerLink.data("customstateid");
                    $('#setPersonStatusCustomStateId').val(customStateType);
                    let setUnitStatusDropdown = $('#PersonnelStatusDropdown');

                    $.get(resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelStatusHtmlForDropdownByStateId?customStateId=' + customStateType, function (data) {
                        $(setUnitStatusDropdown).empty().append(data);


                        let setPersonnelStatusDestinationDropdown = $('#PersonnelStatusDestinationDropdown');
                        $.get(resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelStatusDestinationHtmlForDropdown?customStateId=' + $('#setPersonStatusCustomStateId').val() + '&customStatusDetailId=' + $("#PersonnelStatusDropdown").val(), function (data) {
                            $(setPersonnelStatusDestinationDropdown).empty().append(data);
                        });
                    });
                });

                $("#PersonnelStatusDropdown").change(function () {
                    let status = this.value;

                    let setUnitStatusDestinationDropdown = $('#PersonnelStatusDestinationDropdown');
                    $.get(resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelStatusDestinationHtmlForDropdown?customStateId=' + $('#setPersonStatusCustomStateId').val() + '&customStatusDetailId=' + status, function (data) {
                        $(setUnitStatusDestinationDropdown).empty().append(data);
                    });
                });

                $(document).on('click', '#savingPersonnelStatusButton',
                    function () {
                        $('#savingPersonnelStatusButtonLoader').show();
                        $('#savingPersonnelStatusButton').hide();
                        $.get(resgrid.absoluteBaseUrl + '/User/Personnel/SetActionForUser?userId=' + $('#setPersonStatusUserId').val() + '&actionType=' + $("#PersonnelStatusDropdown").val() + '&destination=' + $('#PersonnelStatusDestinationDropdown').val() + '&note=' + encodeURI($('#PersonnelStatusNote').val()), function (data) {
                            location.reload();
                        });
                    });


                // Set Staffing for 1 person Modal
                $("#setPersonStaffingModal").on('shown.bs.modal', function (e) {
                    let triggerLink = $(e.relatedTarget);
                    let id = triggerLink.data("id");
                    $('#setPersonStaffingUserId').val(id);
                    let customStateType = triggerLink.data("customstateid");
                    $('#setPersonStaffingCustomStateId').val(customStateType);
                    let setUnitStatusDropdown = $('#PersonnelStaffingDropdown');

                    $.get(resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelStaffingHtmlForDropdownByStateId?customStateId=' + customStateType, function (data) {
                        $(setUnitStatusDropdown).empty().append(data);
                    });
                });

                $(document).on('click', '#savingPersonnelStaffingButton',
                    function () {
                        $('#savingPersonnelStaffingButtonLoader').show();
                        $('#savingPersonnelStaffingButton').hide();
                        $.get(resgrid.absoluteBaseUrl + '/User/Personnel/SetStaffingForUser?userId=' + $('#setPersonStaffingUserId').val() + '&staffing=' + $("#PersonnelStaffingDropdown").val() + '&note=' + encodeURI($('#PersonnelStaffingNote').val()), function (data) {
                            location.reload();
                        });
                    });


                // Set Status for Multiple Personnel Modal
                $("#setSelectedPersonnelStatusModal").on('shown.bs.modal', function (e) {
                    let setUnitStatusDropdown = $('#SelectedPersonnelStatusDropdown');

                    $.get(resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelStatusHtmlForDropdownByStateId?customStateId=' + getSelectedCheckboxStatusDataValue(), function (data) {
                        $(setUnitStatusDropdown).empty().append(data);

                        var setUnitStatusDestinationDropdown = $('#SelectedPersonnelStatusDestinationDropdown');
                        $.get(resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelStatusDestinationHtmlForDropdown?customStateId=' + getSelectedCheckboxStatusDataValue() + '&customStatusDetailId=' + $("#SelectedPersonnelStatusDropdown").val(), function (data) {
                            $(setUnitStatusDestinationDropdown).empty().append(data);
                        });
                    });
                });

                $("#SelectedPersonnelStatusDropdown").change(function () {
                    let status = this.value;

                    let setUnitStatusDestinationDropdown = $('#SelectedPersonnelStatusDestinationDropdown');
                    $.get(resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelStatusDestinationHtmlForDropdown?customStateId=' + getSelectedCheckboxStatusDataValue() + '&customStatusDetailId=' + status, function (data) {
                        $(setUnitStatusDestinationDropdown).empty().append(data);
                    });
                });

                $(document).on('click', '#savingSelectedPersonnelStatusButton',
                    function () {
                        $('#savingSelectedPersonnelStatusButtonLoader').show();
                        $('#savingSelectedPersonnelStatusButton').hide();
                        $.get(resgrid.absoluteBaseUrl + '/User/Personnel/SetUserActionForMultiple?userIds=' + getSelectedUsers() + '&actionType=' + $("#SelectedPersonnelStatusDropdown").val() + '&destination=' + $('#SelectedPersonnelStatusDestinationDropdown').val() + '&note=' + encodeURI($('#SelectedPersonnelStatusNote').val()), function (data) {
                            location.reload();
                        });
                    });


                // Set Staffing for Multiple Personnel Modal
                $("#setSelectedPersonnelStaffingModal").on('shown.bs.modal', function (e) {
                    let setUnitStatusDropdown = $('#SelectedPersonnelStaffingDropdown');

                    $.get(resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelStaffingHtmlForDropdownByStateId?customStateId=' + getSelectedCheckboxStaffingDataValue(), function (data) {
                        $(setUnitStatusDropdown).empty().append(data);
                    });
                });

                $(document).on('click', '#savingSelectedPersonnelStaffingButton',
                    function () {
                        $('#savingSelectedPersonnelStaffingButtonLoader').show();
                        $('#savingSelectedPersonnelStaffingButton').hide();
                        $.get(resgrid.absoluteBaseUrl + '/User/Personnel/SetUserStaffingForMultiple?userIds=' + getSelectedUsers() + '&staffing=' + $("#SelectedPersonnelStaffingDropdown").val() + '&note=' + encodeURI($('#SelectedPersonnelStaffingNote').val()), function (data) {
                            location.reload();
                        });
                    });


                $(document).on('click', '.list-group-item', function (e) {
                    if (e) {
                        $('.personnelTabPannel').each(function (i, el) {
                            $(el).hide();
                        });

                        if (e.target) {
                            $('.list-group-item').each(function (i, el) {
                                if (el.textContent === e.target.textContent)
                                    $(el).css("font-weight", "bold");
                                else
                                    $(el).css("font-weight", "normal");
                            });

                            $("#personnelTab" + e.target.id.replace('TreeGroup_', '')).show();

                            $.fn.dataTable
                                .tables({ visible: true, api: true })
                               .columns.adjust().draw();

                            //let dataTable = $("#personnelTable" + e.target.id.replace('TreeGroup_', '')).DataTable();
                            //if (dataTable) {
                            //    dataTable.columns.adjust().draw();
                            //}
                        }
                    }
                });

                function evaluate() {
                    if (anyCheckboxesSelected()) {
                        $('#multiSelectPersons').show();
                    } else {
                        $('#multiSelectPersons').hide();
                    }
                }

                function anyCheckboxesSelected() {
                    let isChecked = false;

                    $('.selectPersonCheckbox').each(function (i, el) {
                        if ($(el).is(':checked'))
                            isChecked = true;
                    });

                    return isChecked;
                }

                function getSelectedCheckboxStaffingDataValue() {
                    $('[data-staffingid]').each(function (i, el) {
                        if ($(el).is(':checked')) {
                            var checkedStateId = $(el).attr("data-staffingid");

                            if (checkedStateId)
                                checkedValue = checkedStateId;
                        }
                    });

                    return checkedValue;
                }

                function getSelectedCheckboxStatusDataValue() {
                    $('[data-statusid]').each(function (i, el) {
                        if ($(el).is(':checked')) {
                            var checkedStateId = $(el).attr("data-statusid");

                            if (checkedStateId)
                                checkedValue = checkedStateId;
                        }
                    });

                    return checkedValue;
                }

                function getSelectedUsers() {
                    let usersString = '';
                    $('[data-statusid]').each(function (i, el) {
                        if ($(el).is(':checked')) {
                            if (usersString.length > 0) {
                                usersString += '|' + $(el).val();
                            } else {
                                usersString = $(el).val();
                            }
                        }
                    });

                    return usersString;
                }
            });
        })(index = personnel.index || (personnel.index = {}));
    })(personnel = resgrid.personnel || (resgrid.personnel = {}));
})(resgrid || (resgrid = {}));
