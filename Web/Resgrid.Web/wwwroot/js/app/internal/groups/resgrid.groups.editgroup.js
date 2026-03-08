
var resgrid;
(function (resgrid) {
	var groups;
	(function (groups) {
		var editgroup;
		(function (editgroup) {
			$(document).ready(function () {
				var groupUrl = resgrid.absoluteBaseUrl + '/User/Department/GetRecipientsForGrid?filter=3&filterNotInGroup=true&ignoreGroupId=' + $('#EditGroup_DepartmentGroupId').val();

				function showDuplicateError(userName) {
					var alertId = 'groupDuplicateAlert';
					$('#' + alertId).remove();
					var alertHtml = '<div id="' + alertId + '" class="alert alert-danger alert-dismissible" role="alert">' +
						'<button type="button" class="close" data-dismiss="alert" aria-label="Close"><span aria-hidden="true">&times;</span></button>' +
						'<strong>Error:</strong> ' + $('<span>').text(userName).html() + ' is already added to the other list. A user can only be a member or an admin, not both.' +
						'</div>';
					$('.ibox-content form').prepend(alertHtml);
					$('html, body').animate({ scrollTop: 0 }, 'fast');
				}

				function getSelectedIds(selector) {
					return $(selector).val() || [];
				}

				function initGroupSelect2(selector, otherSelector) {
					$(selector).select2({
						placeholder: "Select users...",
						allowClear: true,
						multiple: true,
						ajax: {
							url: groupUrl,
							dataType: 'json',
							processResults: function (data) {
								return { results: $.map(data, function (u) { return { id: u.Id, text: u.Name }; }) };
							}
						}
					}).on('select2:select', function (e) {
						var selectedId = e.params.data.id;
						var selectedName = e.params.data.text;
						var otherIds = getSelectedIds(otherSelector);
						if (otherIds.indexOf(selectedId) !== -1) {
							var currentVals = getSelectedIds(selector).filter(function (v) { return v !== selectedId; });
							$(selector).val(currentVals).trigger('change');
							showDuplicateError(selectedName);
						}
					});
				}

				initGroupSelect2("#groupAdmins", "#groupUsers");
				initGroupSelect2("#groupUsers", "#groupAdmins");
				$.ajax({
					url: resgrid.absoluteBaseUrl + '/User/Groups/GetMembersForGroup?groupId=' + $('#EditGroup_DepartmentGroupId').val() + '&includeAdmins=true&includeNormal=false',
					contentType: 'application/json', type: 'GET'
				}).done(function (data) {
					if (data) {
						data.forEach(function (u) { $("#groupAdmins").append(new Option(u.Name, u.UserId, true, true)); });
						$("#groupAdmins").trigger('change');
					}
				});
				$.ajax({
					url: resgrid.absoluteBaseUrl + '/User/Groups/GetMembersForGroup?groupId=' + $('#EditGroup_DepartmentGroupId').val() + '&includeAdmins=false&includeNormal=true',
					contentType: 'application/json', type: 'GET'
				}).done(function (data) {
					if (data) {
						data.forEach(function (u) { $("#groupUsers").append(new Option(u.Name, u.UserId, true, true)); });
						$("#groupUsers").trigger('change');
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
							//resgrid.showProgress($("#personnelGrid"), false);
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
