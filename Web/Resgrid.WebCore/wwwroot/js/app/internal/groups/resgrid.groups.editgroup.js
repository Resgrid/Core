
var resgrid;
(function (resgrid) {
	var groups;
	(function (groups) {
		var editgroup;
		(function (editgroup) {
			$(document).ready(function () {
				$("#groupAdmins").kendoMultiSelect({
					placeholder: "Select group admins...",
					dataTextField: "Name",
					dataValueField: "Id",
					autoBind: false,
					dataSource: {
						type: "json",
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterNotInGroup=true&ignoreGroupId=' + $('#EditGroup_DepartmentGroupId').val()
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
							read: resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterNotInGroup=true&ignoreGroupId=' + $('#EditGroup_DepartmentGroupId').val()
						}
					}
				});
				$.ajax({
					url: resgrid.absoluteBaseUrl + '/User/Groups/GetMembersForGroup?groupId=' + $('#EditGroup_DepartmentGroupId').val() + '&includeAdmins=true&includeNormal=false',
					contentType: 'application/json',
					type: 'GET'
				}).done(function (data) {
					if (data) {
						var multiSelect = $("#groupAdmins").data("kendoMultiSelect");
						var valuesToAdd = [];
						for (var i = 0; i < data.length; i++) {
							valuesToAdd.push(data[i].UserId);
						}
						multiSelect.value(valuesToAdd);
					}
				});
				$.ajax({
					url: resgrid.absoluteBaseUrl + '/User/Groups/GetMembersForGroup?groupId=' + $('#EditGroup_DepartmentGroupId').val() + '&includeAdmins=false&includeNormal=true',
					contentType: 'application/json',
					type: 'GET'
				}).done(function (data) {
					if (data) {
						var multiSelect = $("#groupUsers").data("kendoMultiSelect");
						var valuesToAdd = [];
						for (var i = 0; i < data.length; i++) {
							valuesToAdd.push(data[i].UserId);
						}
						multiSelect.value(valuesToAdd);
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
								tr += `<td><a class="btn btn-xs btn-primary" onclick='resgrid.groups.editgroup.selectPrinter("${value.Id}","${value.Name.replace(/\//g, "-").replace(/ /g, "_")}");' data-dismiss="modal">Select this Printer</a></td>`;
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
			editgroup.selectPrinter = selectPrinter;
		})(editgroup = groups.editgroup || (groups.editgroup = {}));
	})(groups = resgrid.groups || (resgrid.groups = {}));
})(resgrid || (resgrid = {}));
