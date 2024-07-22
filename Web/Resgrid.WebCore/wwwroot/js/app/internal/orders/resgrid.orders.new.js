var resgrid;
(function (resgrid) {
	var orders;
	(function (orders) {
		var newOrder;
		(function (newOrder) {
			$(document).ready(function () {
				resgrid.common.analytics.track('Resource Orders - New');

				$("#Order_NeededBy").keypress(function (e) {
					e.preventDefault();
				});
                $('#Order_NeededBy').datetimepicker({ step: 60 });

				$("#Order_MeetupDate").keypress(function (e) {
					e.preventDefault();
				});
                $('#Order_MeetupDate').datetimepicker({ step: 15 });

				$("#newOrderForm").submit(function (event) {
					for (var i = 1; i <= resgrid.orders.newOrder.orderCount; i++) {

						var name = $('#itemResource_' + i).val();
						var min = $('#itemMin_' + i).val();
						var max = $('#itemMax_' + i).val();

						if (name === null || name == '') {
							swal({
								title: "Missing Resource Name",
								text: "You need to supply a resource name",
								type: "error",
								showCancelButton: false,
								confirmButtonColor: "#DD6B55",
								confirmButtonText: "Ok",
								closeOnConfirm: false
							});

							return false;
						}

						if (min === null || min == '' || min <= 0) {
							swal({
								title: "Missing or Bad Minimum",
								text: "You need to supply a minimum value and that needs to be above 0",
								type: "error",
								showCancelButton: false,
								confirmButtonColor: "#DD6B55",
								confirmButtonText: "Ok",
								closeOnConfirm: false
							});

							return false;
						}

						if (max === null || max == '' || max <= 0 || min > max) {
							swal({
								title: "Missing or Bad Maximum",
								text: "You need to supply a maxiumum value and that needs to be above the minimum",
								type: "error",
								showCancelButton: false,
								confirmButtonColor: "#DD6B55",
								confirmButtonText: "Ok",
								closeOnConfirm: false
							});

							return false;
						}
					}

					return true;
				});

				resgrid.orders.newOrder.orderCount = 0;
			});

			function addItem() {
				resgrid.orders.newOrder.orderCount++;
				$('#orders tbody').first().append("<tr><td><input type='text' class='form-control' placeholder='Resource Description' id='itemResource_" + newOrder.orderCount + "' name='itemResource_" + newOrder.orderCount + "'></td><td><input type='number' class='form-control' id='itemMin_" + newOrder.orderCount + "' name='itemMin_" + newOrder.orderCount + "' min='1' value='1' style='width:60px;'></td><td><input type='number' class='form-control' id='itemMax_" + newOrder.orderCount + "' name='itemMax_" + newOrder.orderCount + "' min='1' value='1' style='width:60px;'></td><td><input type='text' class='form-control' placeholder='Code' style='width:100px;' id='itemFinancial_" + newOrder.orderCount + "' name='itemFinancial_" + newOrder.orderCount + "'></td><td style='max-width: 215px;'><textarea id='itemNeeds_" + newOrder.orderCount + "' name='itemNeeds_" + newOrder.orderCount + "' rows='4' cols='30'></textarea></td><td style='max-width: 215px;'><textarea id='itemRequirements_" + newOrder.orderCount + "' name='itemRequirements_" + newOrder.orderCount + "' rows='4' cols='30'></textarea></td><td style='text-align:center;'><a onclick='$(this).parent().parent().remove();' class='tip-top' data-original-title='Remove this order item'><i class='fa fa-minus' style='color: red;'></i></a></td></tr>");
			}
			newOrder.addItem = addItem;
		})(newOrder = orders.newOrder || (orders.newOrder = {}));
	})(orders = resgrid.orders || (resgrid.orders = {}));
})(resgrid || (resgrid = {}));
