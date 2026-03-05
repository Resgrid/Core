var resgrid;
(function (resgrid) {
	var orders;
	(function (orders) {
		var settings;
		(function (settings) {
			$(document).ready(function () {
				$("#departmentTypes").select2({
					placeholder: "Select allowed department types...",
					allowClear: true,
					multiple: true,
					ajax: {
						url: resgrid.absoluteBaseUrl + '/User/Department/GetDepartmentTypes',
						dataType: 'json',
						processResults: function (data) {
							return { results: $.map(data, function (t) { return { id: t.Text, text: t.Text }; }) };
						}
					}
				});

				$("#notifyRoles").select2({
					placeholder: "Select roles to notify...",
					allowClear: true,
					multiple: true,
					ajax: {
						url: resgrid.absoluteBaseUrl + '/User/Personnel/GetRoles',
						dataType: 'json',
						processResults: function (data) {
							return { results: $.map(data, function (r) { return { id: r.RoleId, text: r.Name }; }) };
						}
					}
				});
			});
		})(settings = orders.settings || (orders.settings = {}));
	})(orders = resgrid.orders || (resgrid.orders = {}));
})(resgrid || (resgrid = {}));
