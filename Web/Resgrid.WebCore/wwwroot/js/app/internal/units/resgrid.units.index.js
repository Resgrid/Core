
var resgrid;
(function (resgrid) {
	var units;
	(function (units) {
		var index;
		(function (index) {
			$(document).ready(function () {
				resgrid.common.analytics.track('Units List');

				$('input[type="checkbox"]').click(evaluate);
				$("#unitsIndexList").kendoGrid({
					dataSource: {
						type: "json",
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Units/GetUnitsList'
						},
						schema: {
							model: {
								fields: {
									UnitId: { type: "number" },
									Name: { type: "string" },
									Type: { type: "string" },
									Station: { type: "string" },
									StateId: { type: "number" },
									State: { type: "string" },
									StateColor: { type: "string" },
									TextColor: { type: "string" },
									Timestamp: { type: "string" }
								}
							}
						},
						pageSize: 50
					},
					//height: 400,
					filterable: true,
					sortable: true,
					scrollable: true,
					pageable: {
						refresh: true,
						pageSizes: true,
						buttonCount: 5
					},
					columns: [
						"Name",
						"Type",
						"Station",
						{
							field: "State",
							title: "State",
							template: kendo.template($("#state-template").html())
						},
						"Timestamp",
						{
							field: "UnitId",
							title: " ",
							filterable: false,
							sortable: false,
							template: kendo.template($("#unitstatus-template").html())
						},
						{
							field: "UnitId",
							title: "Actions",
							filterable: false,
							sortable: false,
							width: 250,
							template: kendo.template($("#unitcommand-template").html())
						}
					]
				});

				$(document).on('click', '.stateDropdown',
					function () {
						var unitId = $(this).attr("data-id");
						$(".unitStateList_" + unitId).remove();
						var that = this;
						$.get(resgrid.absoluteBaseUrl + '/User/Units/GetUnitOptionsDropdown?unitId=' + unitId, function (data) {
							$(that).after(data);
						});
					});

				$(document).on('click', '.multiStateDropdown',
					function () {
						$(".unitStateList_" + getSelectedCheckboxDataValue()).remove();
						var that = this;
						$.get(resgrid.absoluteBaseUrl + '/User/Units/GetUnitOptionsDropdownForStates?stateId=' + getSelectedCheckboxDataValue() + '&units=' + getSelectedUnits(), function (data) {
							$(that).after(data);
						});
					});

				function evaluate() {
					var item = $(this);
					var checkedStateId = item.attr("data-customState");

					if (item.is(":checked")) {
						$('[data-customState]').each(function (i, el) {
							var customStateId = $(el).attr("data-customState");

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
					var isChecked = false;

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
					var unitsString = '';
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
			});
		})(index = units.index || (units.index = {}));
	})(units = resgrid.units || (resgrid.units = {}));
})(resgrid || (resgrid = {}));
