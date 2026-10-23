var resgrid;
(function (resgrid) {
	var links;
	(function (links) {
		var viewData;
		(function (viewData) {
			$(document).ready(function () {
				$("#callsList").DataTable({
					ajax: {
						url: resgrid.absoluteBaseUrl + '/User/Links/GetActiveCallsList?linkId=' + linkId,
						dataSrc: ''
					},
					pageLength: 50,
					columns: [
						{ data: 'Number', title: 'Number', type: 'string', render: resgrid.callNumberCell },
						{ data: 'Name', title: 'Name', render: resgrid.textCell },
						{ data: 'Timestamp', title: 'Timestamp' },
						{
							data: null,
							title: 'Priority',
							orderable: false,
							render: function (data, type, row) {
								return '<span style="background-color:' + resgrid.escapeHtml(row.Color) + ';color:#fff;padding:2px 6px;border-radius:3px;">' + resgrid.escapeHtml(row.Priority) + '</span>';
							}
						}
					]
				});

				$("#unitsList").DataTable({
					ajax: {
						url: resgrid.absoluteBaseUrl + '/User/Links/GetUnitsList?linkId=' + linkId,
						dataSrc: ''
					},
					pageLength: 50,
					columns: [
						{ data: 'Name', title: 'Name', render: resgrid.textCell },
						{ data: 'Type', title: 'Type', render: resgrid.textCell },
						{ data: 'Station', title: 'Station', render: resgrid.textCell },
						{
							data: null,
							title: 'State',
							orderable: false,
							render: function (data, type, row) {
								return '<span style="background-color:' + resgrid.escapeHtml(row.StateColor) + ';color:' + resgrid.escapeHtml(row.TextColor) + ';padding:2px 6px;border-radius:3px;">' + resgrid.escapeHtml(row.State) + '</span>';
							}
						},
						{ data: 'Timestamp', title: 'Timestamp' }
					]
				});

				$("#personnelList").DataTable({
					ajax: {
						url: resgrid.absoluteBaseUrl + '/User/Links/GetPersonnelList?linkId=' + linkId,
						dataSrc: ''
					},
					pageLength: 50,
					columns: [
						{ data: 'Name', title: 'Name', render: resgrid.textCell },
						{ data: 'Group', title: 'Group', render: resgrid.textCell },
						{ data: 'Roles', title: 'Roles', render: resgrid.textCell },
						{ data: 'State', title: 'Staffing', render: resgrid.textCell },
						{ data: 'Status', title: 'Status', render: resgrid.textCell },
						{ data: 'UpdatedDate', title: 'Timestamp' },
						{ data: 'Eta', title: 'ETA', render: resgrid.textCell }
					]
				});
			});
		})(viewData = links.viewData || (links.viewData = {}));
	})(links = resgrid.links || (resgrid.links = {}));
})(resgrid || (resgrid = {}));
