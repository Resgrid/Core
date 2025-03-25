var resgrid;
(function (resgrid) {
	var orders;
	(function (orders) {
		var fill;
		(function (fill) {
			$(document).ready(function () {
				resgrid.common.analytics.track('Resource Orders - Fill');

				resgrid.orders.fill.itemCount = 0;
			});

			function addItem(element) {
				$('#' + element).first().append("<div class='col-md-2'><dl><dt>Contact Name</dt><dd><input type='text' class='form-control' placeholder='Contact name' id='contactName_" + fill.itemCount + "' name='contactName_" + fill.itemCount + "'></dd></dl></div><div class='col-md-2'><dl><dt>Contact Info</dt><dd><input type='text' class='form-control' placeholder='Contact number' id='contactNumber_" + fill.itemCount + "' name='contactNumber_" + fill.itemCount + "'></dd></dl></div><div class='col-md-3'><dl><dt>Note</dt><dd><input type='text' class='form-control' placeholder='Note for this fill' id='note_" + fill.itemCount + "' name='note_" + fill.itemCount + "'></dd></dl></div><div class='col-md-2'><dl><dt>Lead User</dt><dd><select id='leadUser_" + fill.itemCount + "' name='leadUser_" + fill.itemCount + "' style='padding-left: 0; width: 100%;'></select></dd></dl></div><div class='col-md-2'><dl><dt>Fill Units</dt><dd><select id='units_" + fill.itemCount + "' name='units_" + fill.itemCount + "'></select></dd></dl></div><div class='col-md-1'><dl><dt>&nbsp;</dt><dd><a class='btn btn-xs btn-success' onclick='resgrid.orders.fill.saveFill(event, " + fill.itemCount + ");'>Submit Fill</a></dd></dl></div>");

				$("#" + "units_" + fill.itemCount).kendoMultiSelect({
					placeholder: "Select units to fill in this item...",
					dataTextField: "Name",
					dataValueField: "UnitId",
					autoBind: false,
					dataSource: {
						type: "json",
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Units/GetUnits'
						}
					}
				});

				$("#" + "leadUser_" + fill.itemCount).kendoDropDownList({
					placeholder: "Select the lead User...",
					dataTextField: "Name",
					dataValueField: "UserId",
					autoBind: false,
					dataSource: {
						type: "json",
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Personnel/GetPersonnelForGrid'
						}
					}
				});

				resgrid.orders.fill.itemCount++;
			}
			fill.addItem = addItem;

			function saveFill(event, count) {
				if ($('#contactName_' + count).val() === null || $('#contactName_' + count).val() == '') {
					swal({
						title: "Missing Value",
						text: "You need to supply a Contact Name",
						type: "error",
						showCancelButton: false,
						confirmButtonColor: "#DD6B55",
						confirmButtonText: "Ok",
						closeOnConfirm: false
					});

					return;
				}

				if ($('#contactNumber_' + count).val() === null || $('#contactNumber_' + count).val() == '') {
					swal({
						title: "Missing Value",
						text: "You need to supply a Contact Number (Info)",
						type: "error",
						showCancelButton: false,
						confirmButtonColor: "#DD6B55",
						confirmButtonText: "Ok",
						closeOnConfirm: false
					});

					return;
				}

				if ($('#leadUser_' + count).val() === null || $('#leadUser_' + count).val() == '') {
					swal({
						title: "Missing Value",
						text: "You need to supply a Lead User from the dropdown",
						type: "error",
						showCancelButton: false,
						confirmButtonColor: "#DD6B55",
						confirmButtonText: "Ok",
						closeOnConfirm: false
					});

					return;
				}

				if ($('#units_' + count).val() === null || $('#units_' + count).val() == '') {
					swal({
						title: "Missing Value",
						text: "You need to select the units you want to use to fill this item",
						type: "error",
						showCancelButton: false,
						confirmButtonColor: "#DD6B55",
						confirmButtonText: "Ok",
						closeOnConfirm: false
					});

					return;
				}

				var units = $('#units_' + count).val();
				if (units.length > window['unitFillItem_' + count]) {
					swal({
						title: "Too Many Units",
						text: "You have selected too many units to fill this over what was needed. The item only needs " + window['unitFillItem_' + count] + " units and you have selected " + units.length + " to fill this item.",
						type: "error",
						showCancelButton: false,
						confirmButtonColor: "#DD6B55",
						confirmButtonText: "Ok",
						closeOnConfirm: false
					});

					return;
				}

				if (units.length != window['unitFillItem_' + count] && allowPartialFills === 'False') {
					swal({
						title: "Partial Fills Not Allowed",
						text: "This order requires the entire request to be filled by one fill. The item needs " + window['unitFillItem_' + count] + " units and you have selected " + units.length + " to fill this item.",
						type: "error",
						showCancelButton: false,
						confirmButtonColor: "#DD6B55",
						confirmButtonText: "Ok",
						closeOnConfirm: false
					});

					return;
				}

				$.post("/User/Orders/FillItem", {
					Id: window['unitFillId_' + count],
					Name: $('#contactName_' + count).val(),
					Number: $('#contactNumber_' + count).val(),
					Note: $('#note_' + count).val(),
					LeadUserId: $('#leadUser_' + count).val(),
					Units: $('#units_' + count).val()
				}, function (data) {
					if (data) {
						window.location.replace("/User/Orders/FillItem?id=" + data.id + "&error=" + data.error + "&errorMessage=" + data.errorMessage);
					}
				});
			}
			fill.saveFill = saveFill;
		})(fill = orders.fill || (orders.fill = {}));
	})(orders = resgrid.orders || (resgrid.orders = {}));
})(resgrid || (resgrid = {}));
