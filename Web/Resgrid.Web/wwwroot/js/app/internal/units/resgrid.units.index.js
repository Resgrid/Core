
var resgrid;
(function (resgrid) {
    var units;
    (function (units) {
        var index;
        (function (index) {
            $(document).ready(function () {
                resgrid.common.analytics.track('Units List');

                $('.table').DataTable();
                $('#tree').bstreeview({ data: treeData });
                $('input[type="checkbox"]').click(evaluate);
                $('#TreeGroup_-1').css("font-weight", "bold");

                // Set Status for 1 unit Modal
                $("#setUnitStatusModal").on('shown.bs.modal', function (e) {
                    let triggerLink = $(e.relatedTarget);
                    let id = triggerLink.data("id");
                    $('#setUnitStateUnitId').val(id);
                    let customStateType = triggerLink.data("customstateid");
                    $('#setUnitStateCustomStateId').val(customStateType);
                    let setUnitStatusDropdown = $('#UnitStatusDropdown');

                    $.get(resgrid.absoluteBaseUrl + '/User/Units/GetUnitStatusHtmlForDropdownByStateId?customStateId=' + customStateType, function (data) {
                        $(setUnitStatusDropdown).empty().append(data);


                        let setUnitStatusDestinationDropdown = $('#UnitStatusDestinationDropdown');
                        $.get(resgrid.absoluteBaseUrl + '/User/Units/GetUnitStatusDestinationHtmlForDropdown?customStateId=' + $('#setUnitStateCustomStateId').val() + '&customStatusDetailId=' + $("#UnitStatusDropdown").val(), function (data) {
                            $(setUnitStatusDestinationDropdown).empty().append(data);
                        });
                    });
                });

                $("#UnitStatusDropdown").change(function () {
                    let status = this.value;

                    let setUnitStatusDestinationDropdown = $('#UnitStatusDestinationDropdown');

                    $.get(resgrid.absoluteBaseUrl + '/User/Units/GetUnitStatusDestinationHtmlForDropdown?customStateId=' + $('#setUnitStateCustomStateId').val() + '&customStatusDetailId=' + status, function (data) {
                        $(setUnitStatusDestinationDropdown).empty().append(data);
                    });
                });

                $(document).on('click', '#savingUnitStatusButton',
                    function () {
                        $('#savingUnitStatusButtonLoader').show();
                        $('#savingUnitStatusButton').hide();
                        $.get(resgrid.absoluteBaseUrl + '/User/Units/SetUnitStateWithDest?unitId=' + $('#setUnitStateUnitId').val() + '&stateType=' + $("#UnitStatusDropdown").val() + '&type=2&destination=' + $('#UnitStatusDestinationDropdown').val() + '&note=' + encodeURI($('#UnitStatusNote').val()), function (data) {
                            location.reload();
                        });
                    });

                // Set Status for Multiple Units Modal
                $("#setSelectedUnitStatusModal").on('shown.bs.modal', function (e) {
                    let triggerLink = $(e.relatedTarget);
                    let setUnitStatusDropdown = $('#SelectedUnitStatusDropdown');
                    let stateId = getSelectedCheckboxDataValue();

                    $.get(resgrid.absoluteBaseUrl + '/User/Units/GetUnitStatusHtmlForDropdownByStateId?customStateId=' + getSelectedCheckboxDataValue(), function (data) {
                        $(setUnitStatusDropdown).empty().append(data);


                        var setUnitStatusDestinationDropdown = $('#SelectedUnitStatusDestinationDropdown');
                        $.get(resgrid.absoluteBaseUrl + '/User/Units/GetUnitStatusDestinationHtmlForDropdown?customStateId=' + getSelectedCheckboxDataValue() + '&customStatusDetailId=' + $("#SelectedUnitStatusDropdown").val(), function (data) {
                            $(setUnitStatusDestinationDropdown).empty().append(data);
                        });
                    });
                });

                $("#SelectedUnitStatusDropdown").change(function () {
                    let status = this.value;

                    let setUnitStatusDestinationDropdown = $('#SelectedUnitStatusDestinationDropdown');

                    $.get(resgrid.absoluteBaseUrl + '/User/Units/GetUnitStatusDestinationHtmlForDropdown?customStateId=' + getSelectedCheckboxDataValue() + '&customStatusDetailId=' + status, function (data) {
                        $(setUnitStatusDestinationDropdown).empty().append(data);
                    });
                });

                $(document).on('click', '#savingSelectedUnitStatusButton',
                    function () {
                        $('#savingSelectedUnitStatusButtonLoader').show();
                        $('#savingSelectedUnitStatusButton').hide();
                        $.get(resgrid.absoluteBaseUrl + '/User/Units/SetUnitStateForMultiple?unitIds=' + getSelectedUnits() + '&stateType=' + $("#SelectedUnitStatusDropdown").val() + '&type=2&destination=' + $('#SelectedUnitStatusDestinationDropdown').val() + '&note=' + encodeURI($('#SelectedUnitStatusNote').val()), function (data) {
                            location.reload();
                        });
                    });

                $(document).on('click', '.multiStateDropdown',
                    function () {
                        $(".unitStateList_" + getSelectedCheckboxDataValue()).remove();
                        let that = this;
                        $.get(resgrid.absoluteBaseUrl + '/User/Units/GetUnitOptionsDropdownForStates?stateId=' + getSelectedCheckboxDataValue() + '&units=' + getSelectedUnits(), function (data) {
                            $(that).after(data);
                        });
                    });

                function evaluate() {
                    let item = $(this);
                    let checkedStateId = item.attr("data-customState");

                    if (item.is(":checked")) {
                        $('[data-customState]').each(function (i, el) {
                            let customStateId = $(el).attr("data-customState");

                            if (customStateId != checkedStateId) {
                                $(el).attr('checked', false);
                                $(el).attr("disabled", true);
                            } else {
                                $(el).removeAttr("disabled");
                            }
                        });
                    } else {
                        if (!anyCheckboxesSelected()) {
                            $('[data-customState]').each(function (i, el) {
                                $(el).removeAttr("disabled");
                            });
                        } else {
                            checkedStateId = getSelectedCheckboxDataValue();
                            $('[data-customState]').each(function (i, el) {
                                var customStateId = $(el).attr("data-customState");

                                if (customStateId != checkedStateId) {
                                    $(el).attr('checked', false);
                                    $(el).attr("disabled", true);
                                } else {
                                    $(el).removeAttr("disabled");
                                }
                            });
                        }
                    }

                    if (anyCheckboxesSelected()) {
                        $('#multiSelectUnits').show();
                    } else {
                        $('#multiSelectUnits').hide();
                    }
                }
                index.evaluate = evaluate;

                function anyCheckboxesSelected() {
                    let isChecked = false;

                    $('[data-customState]').each(function (i, el) {
                        if ($(el).is(':checked'))
                            isChecked = true;
                    });

                    return isChecked;

                    var checkedValue;
                }

                function getSelectedCheckboxDataValue() {
                    $('[data-customState]').each(function (i, el) {
                        if ($(el).is(':checked')) {
                            var checkedStateId = $(el).attr("data-customState");

                            if (checkedStateId)
                                checkedValue = checkedStateId;
                        }
                    });

                    return checkedValue;
                }

                function getSelectedUnits() {
                    let unitsString = '';
                    $('[data-customState]').each(function (i, el) {
                        if ($(el).is(':checked')) {
                            if (unitsString.length > 0) {
                                unitsString += '|' + $(el).val();
                            } else {
                                unitsString = $(el).val();
                            }
                        }
                    });

                    return unitsString;
                }

                $(document).on('click', '.list-group-item', function (e) {
                    if (e) {
                        $('.unitTabPannel').each(function (i, el) {
                            $(el).hide();
                        });

                        if (e.target) {
                            $('.list-group-item').each(function (i, el) {
                                if (el.textContent === e.target.textContent)
                                    $(el).css("font-weight", "bold");
                                else
                                    $(el).css("font-weight", "normal");
                            });

                            $("#unitsTab" + e.target.id.replace('TreeGroup_', '')).show();

                            $.fn.dataTable
                                .tables({ visible: true, api: true })
                                .columns.adjust().draw();
                        }
                    }
                });
            });
        })(index = units.index || (units.index = {}));
    })(units = resgrid.units || (resgrid.units = {}));
})(resgrid || (resgrid = {}));
