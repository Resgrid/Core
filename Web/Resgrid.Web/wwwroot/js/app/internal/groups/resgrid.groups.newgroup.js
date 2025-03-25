
var resgrid;
(function (resgrid) {
	var groups;
	(function (groups) {
		var newgroup;
		(function (newgroup) {
			$(document).ready(function () {
				if ($('.groupTypeOrgRadio').is(':checked')) {
					$("#ParentFields").show();
					$("#StationAddress").hide();
				}
				else if ($('.groupTypeStationRadio').is(':checked')) {
					$("#ParentFields").hide();
					$("#StationAddress").show();
				}
				else {
					$("#ParentFields").hide();
					$("#StationAddress").hide();
				}
				$("#groupAdmins").kendoMultiSelect({
					placeholder: "Select group admins...",
					dataTextField: "Name",
					dataValueField: "Id",
					autoBind: false,
					dataSource: {
						type: "json",
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterNotInGroup=true'
						}
					}
				});
				$("#groupUsers").kendoMultiSelect({
					placeholder: "Select group users...",
					dataTextField: "Name",
					dataValueField: "Id",
					autoBind: false,
					dataSource: {
						type: "json",
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterNotInGroup=true'
						}
					}
				});
				$("#getPrintersButton").click(function () {
					if ($('#apiKey').val()) {
						$.ajax({
							url: resgrid.absoluteBaseUrl + '/User/Department/GetPrinterNetPrinters?key=' + $('#apiKey').val(),
							contentType: 'application/json; charset=utf-8',
							type: 'GET'
						}).done(function (results) {
							$('#printersTableBody').empty();
							var tableHtml;
							$.each(results, function (index, value) {
								var tr = '<tr>';
								tr += '<td>' + value.Name + " on " + value.Computer.Name + '</td>';
								tr += `<td><a class="btn btn-xs btn-primary" onclick='resgrid.groups.newgroup.selectPrinter("${value.Id}","${value.Name.replace(/\//g, "-").replace(/ /g, "_")}");' data-dismiss="modal">Select this Printer</a></td>`;
								tr += '</tr>';
								tableHtml += tr;
							});
							//kendo.ui.progress($("#personnelGrid"), false);
							$('#printersTableBody').html(tableHtml);
						});
					}
				});
			});
			function selectPrinter(printerId, printerName) {
				$("#PrinterId").val(printerId);
				$("#PrinterApiKey").val($('#apiKey').val());
				$("#PrinterName").val(printerName);
			}
			newgroup.selectPrinter = selectPrinter;
			function showParentFields() {
				$("#ParentFields").show();
				$("#StationAddress").hide();
			}
			newgroup.showParentFields = showParentFields;
			function showAddressFields() {
				$("#ParentFields").hide();
				$("#StationAddress").show();
			}
			newgroup.showAddressFields = showAddressFields;
		})(newgroup = groups.newgroup || (groups.newgroup = {}));
	})(groups = resgrid.groups || (resgrid.groups = {}));
})(resgrid || (resgrid = {}));
