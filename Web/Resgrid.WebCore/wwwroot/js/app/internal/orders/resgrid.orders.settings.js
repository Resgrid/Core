var resgrid;
(function (resgrid) {
	var orders;
	(function (orders) {
		var settings;
		(function (settings) {
			$(document).ready(function () {

				$("#departmentTypes").kendoMultiSelect({
					placeholder: "Select allowed department types...",
					dataTextField: "Text",
					dataValueField: "Text",
					autoBind: false,
					dataSource: {
						type: "json",
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Department/GetDepartmentTypes'
						}
					}
				});

				$("#notifyRoles").kendoMultiSelect({
					placeholder: "Select roles to notify...",
					dataTextField: "Name",
					dataValueField: "RoleId",
					autoBind: false,
					dataSource: {
						type: "json",
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Personnel/GetRoles'
						}
					}
				});
			});
		})(settings = orders.settings || (orders.settings = {}));
	})(orders = resgrid.orders || (resgrid.orders = {}));
})(resgrid || (resgrid = {}));
