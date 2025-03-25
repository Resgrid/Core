var resgrid;
(function (resgrid) {
	var orders;
	(function (orders) {
		var index;
		(function (index) {
			$(document).ready(function () {
				resgrid.common.analytics.track('Resource Orders List');
				$("#yourOrdersList").kendoGrid({
					dataSource: {
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Orders/GetYourOrders'
						},
						schema: {
							model: {
								fields: {
									Id: { type: "number" },
									DepartmentId: { type: "number" },
									Type: { type: "number" },
									AllowPartialFills: { type: "boolean" },
									Title: { type: "string" },
									IncidentNumber: { type: "string" },
									IncidentName: { type: "string" },
									IncidentAddress: { type: "string" },
									IncidentLatitude: { type: "number" },
									IncidentLongitude: { type: "number" },
									Summary: { type: "string" },
									OpenDate: { type: "string" },
									NeededBy: { type: "string" },
									MeetupDate: { type: "string" },
									CloseDate: { type: "string" },
									ContactName: { type: "string" },
									ContactNumber: { type: "string" },
									SpecialInstructions: { type: "string" },
									MeetupLocation: { type: "string" },
									FinancialCode: { type: "string" },
									AutomaticFillAcceptance: { type: "string" },
									Visibility: { type: "number" },
									Range: { type: "number" },
									OriginLatitude: { type: "number" },
									OriginLongitude: { type: "number" },
									Status: { type: "string" },
									ResourceOrderCount: { type: "number" },
									TotalUnitsOrdered: { type: "number" },
									TotalUntisFilled: { type: "number" }
								}
							}
						},
						pageSize: 50
					},
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
							field: "Title",
							title: "Title"
						},
						{
							field: "OpenDate",
							title: "Open Date"
						},
						{
							field: "Status",
							title: "Status"
						},
						{
							field: "VisibilityName",
							title: "Visibility"
						},
						{
							field: "Id",
							title: "Actions",
							filterable: false,
							sortable: false,
							width: 220,
							template: kendo.template($("#command-template").html())
						}
					]
				});


				$("#othersOrdersList").kendoGrid({
					dataSource: {
						transport: {
							read: resgrid.absoluteBaseUrl + '/User/Orders/GetAvailableOrders'
						},
						schema: {
							model: {
								fields: {
									Id: { type: "number" },
									DepartmentId: { type: "number" },
									Type: { type: "number" },
									AllowPartialFills: { type: "boolean" },
									Title: { type: "string" },
									IncidentNumber: { type: "string" },
									IncidentName: { type: "string" },
									IncidentAddress: { type: "string" },
									IncidentLatitude: { type: "number" },
									IncidentLongitude: { type: "number" },
									Summary: { type: "string" },
									OpenDate: { type: "string" },
									NeededBy: { type: "string" },
									MeetupDate: { type: "string" },
									CloseDate: { type: "string" },
									ContactName: { type: "string" },
									ContactNumber: { type: "string" },
									SpecialInstructions: { type: "string" },
									MeetupLocation: { type: "string" },
									FinancialCode: { type: "string" },
									AutomaticFillAcceptance: { type: "string" },
									Visibility: { type: "number" },
									Range: { type: "number" },
									OriginLatitude: { type: "number" },
									OriginLongitude: { type: "number" },
									Status: { type: "string" },
									ResourceOrderCount: { type: "number" },
									TotalUnitsOrdered: { type: "number" },
									TotalUntisFilled: { type: "number" }
								}
							}
						},
						pageSize: 50
					},
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
							field: "Name",
							title: "Name"
						},
						{
							field: "OpenDate",
							title: "Open Date"
						},
						{
							field: "Status",
							title: "Status"
						},
						{
							field: "VisibilityName",
							title: "Visibility"
						},
						{
							field: "Id",
							title: "Actions",
							filterable: false,
							sortable: false,
							width: 220,
							template: kendo.template($("#command-template").html())
						}
					]
				});

			});
		})(index = orders.index || (orders.index = {}));
	})(orders = resgrid.orders || (resgrid.orders = {}));
})(resgrid || (resgrid = {}));
