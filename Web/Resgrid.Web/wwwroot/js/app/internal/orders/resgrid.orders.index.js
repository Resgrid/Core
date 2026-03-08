var resgrid;
(function (resgrid) {
	var orders;
	(function (orders) {
		var index;
		(function (index) {
			$(document).ready(function () {
				resgrid.common.analytics.track('Resource Orders List');

				$("#yourOrdersList").DataTable({
					ajax: {
						url: resgrid.absoluteBaseUrl + '/User/Orders/GetYourOrders',
						dataSrc: ''
					},
					pageLength: 50,
					columns: [
						{ data: 'Title', title: 'Title' },
						{ data: 'OpenDate', title: 'Open Date' },
						{ data: 'Status', title: 'Status' },
						{ data: 'VisibilityName', title: 'Visibility' },
						{
							data: 'Id',
							title: 'Actions',
							orderable: false,
							searchable: false,
							render: function (data, type, row) {
								return '<a class="btn btn-sm btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Orders/ViewOrder?orderId=' + data + '">View</a> ' +
									'<a class="btn btn-sm btn-info" href="' + resgrid.absoluteBaseUrl + '/User/Orders/EditOrder?orderId=' + data + '">Edit</a> ' +
									'<a class="btn btn-sm btn-danger" href="' + resgrid.absoluteBaseUrl + '/User/Orders/DeleteOrder?orderId=' + data + '">Delete</a>';
							}
						}
					]
				});

				$("#othersOrdersList").DataTable({
					ajax: {
						url: resgrid.absoluteBaseUrl + '/User/Orders/GetAvailableOrders',
						dataSrc: ''
					},
					pageLength: 50,
					columns: [
						{ data: 'Title', title: 'Title' },
						{ data: 'OpenDate', title: 'Open Date' },
						{ data: 'Status', title: 'Status' },
						{ data: 'VisibilityName', title: 'Visibility' },
						{
							data: 'Id',
							title: 'Actions',
							orderable: false,
							searchable: false,
							render: function (data, type, row) {
								return '<a class="btn btn-sm btn-primary" href="' + resgrid.absoluteBaseUrl + '/User/Orders/ViewOrder?orderId=' + data + '">View</a> ' +
									'<a class="btn btn-sm btn-success" href="' + resgrid.absoluteBaseUrl + '/User/Orders/FillOrder?orderId=' + data + '">Fill Order</a>';
							}
						}
					]
				});
			});
		})(index = orders.index || (orders.index = {}));
	})(orders = resgrid.orders || (resgrid.orders = {}));
})(resgrid || (resgrid = {}));
