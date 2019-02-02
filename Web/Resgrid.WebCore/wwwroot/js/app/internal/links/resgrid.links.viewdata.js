var resgrid;
(function (resgrid) {
	var links;
	(function (links) {
		var viewData;
		(function (viewData) {
			$(document).ready(function () {
				$("#callsList").kendoGrid({
					dataSource: {
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Links/GetActiveCallsList?linkId=' + linkId
						},
						schema: {
							model: {
								fields: {
									CallId: { type: "number" },
									Number: { type: "string" },
									Priority: { type: "string" },
									Color: { type: "string" },
									Name: { type: "string" },
									State: { type: "string" },
									StateColor: { type: "string" },
									Address: { type: "string" },
									Timestamp: { type: "string" },
									CanDeleteCall: { type: "boolean" }
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
						{
							field: "Number",
							title: "Number",
							width: 100
						},
						"Name",
						{
							field: "Timestamp",
							title: "Timestamp",
							width: 175
						},
						{
							field: "Priority",
							title: "Priority",
							width: 100,
							template: kendo.template($("#callPriority-template").html())
						}
					]
				});
				$("#unitsList").kendoGrid({
					dataSource: {
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Links/GetUnitsList?linkId=' + linkId
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
							filterable: false,
							template: kendo.template($("#state-template").html())
						},
						"Timestamp"
					]
				});

				$("#personnelList").kendoGrid({
					dataSource: {
						//type: "json",
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Links/GetPersonnelList?linkId=' + linkId
						},
						schema: {
							model: {
								fields: {
									Name: { type: "string" },
									Group: { type: "string" },
									State: { type: "string" },
									Status: { type: "string" },
									UserId: { type: "string" },
									Roles: { type: "string" },
									UpdatedDate: { type: "string" },
									Eta: { type: "string" }
								}
							}
						},
						pageSize: 50
					},
					filterable: true,
					sortable: true,
					scrollable: true,
					//dataBound: function () {  },
					pageable: {
						refresh: true,
						pageSizes: true,
						buttonCount: 5
					},
					columns: [
						{
							field: "Name",
							title: "Name"
						},
						{
							field: "Group",
							title: "Group"
						},
						{
							field: "Roles",
							title: "Roles"
						},
						{
							field: "State",
							title: "Staffing"
						},
						{
							field: "Status",
							title: "Status",
							width: 130
						},
						{
							field: "UpdatedDate",
							title: "Timestamp",
							width: 160
						},
						{
							field: "Eta",
							title: "ETA"
						}
					]
				});
			});
		})(viewData = links.viewData || (links.viewData = {}));
	})(links = resgrid.links || (resgrid.links = {}));
})(resgrid || (resgrid = {}));