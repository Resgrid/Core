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
						{ data: 'Number', title: 'Number' },
						{ data: 'Name', title: 'Name' },
						{ data: 'Timestamp', title: 'Timestamp' },
						{
							data: null,
							title: 'Priority',
							orderable: false,
							render: function (data, type, row) {
								return '<span style="background-color:' + row.Color + ';color:#fff;padding:2px 6px;border-radius:3px;">' + row.Priority + '</span>';
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
						{ data: 'Name', title: 'Name' },
						{ data: 'Type', title: 'Type' },
						{ data: 'Station', title: 'Station' },
						{
							data: null,
							title: 'State',
							orderable: false,
							render: function (data, type, row) {
								return '<span style="background-color:' + row.StateColor + ';color:' + row.TextColor + ';padding:2px 6px;border-radius:3px;">' + row.State + '</span>';
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
						{ data: 'Name', title: 'Name' },
						{ data: 'Group', title: 'Group' },
						{ data: 'Roles', title: 'Roles' },
						{ data: 'State', title: 'Staffing' },
						{ data: 'Status', title: 'Status' },
						{ data: 'UpdatedDate', title: 'Timestamp' },
						{ data: 'Eta', title: 'ETA' }
					]
				});
			});
		})(viewData = links.viewData || (links.viewData = {}));
	})(links = resgrid.links || (resgrid.links = {}));
})(resgrid || (resgrid = {}));
