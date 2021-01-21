var resgrid;
(function (resgrid) {
	var department;
	(function (department) {
		var settings;
		(function (settings) {
			$(document).ready(function () {
				$('select').select2();
				$('#TimeToResetStaffing').kendoTimePicker({
					interval: 15
				});
				$('#TimeToResetStatus').kendoTimePicker({
					interval: 15
				});
				$("#MapZoomLevel").kendoNumericTextBox({
					format: "#",
					min: 0,
					max: 15,
					step: 1
				});
				$("#RefreshTime").kendoNumericTextBox({
					format: "#",
					min: 30,
					max: 900,
					step: 15
				});
				$('#Department_Address_Address1').blur(function () {
					validateDepartmentAddress();
				});
				$('#Department_Address_City').blur(function () {
					validateDepartmentAddress();
				});
				$('#Department_Address_State').blur(function () {
					validateDepartmentAddress();
				});
				$('#Department_Address_PostalCode').blur(function () {
					validateDepartmentAddress();
				});
			});
			function validateDepartmentAddress() {
				var address = $('#Department_Address_Address1').val();
				var city = $('#Department_Address_City').val();
				var state = $('#Department_Address_State').val();
				var postalCode = $('#Department_Address_PostalCode').val();
				var country = $('#Department_Address_Country').val();
				if (address && city && state && postalCode && country) {
					$.ajax({
						url: resgrid.absoluteBaseUrl + '/User/Department/ValidateAddress',
						data: JSON.stringify({
							Address1: address,
							City: city,
							State: state,
							PostalCode: postalCode,
							Country: country
						}),
						contentType: 'application/json',
						type: 'POST'
					}).done(function (data) {
						if (data) {
							$('#departmentAddressBlock').removeClass('has-error has-success').addClass('has-error');
							$('#departmentAddressFailure').show();
						}
						else {
							$('#departmentAddressFailure').hide();
							$('#departmentAddressBlock').removeClass('has-error has-success');
						}
					});
				}
			}
			settings.validateDepartmentAddress = validateDepartmentAddress;

			function refreshDepartmentCache() {
				swal({
					title: "Are you sure?",
					text: "This request can take up to 15 minutes to process. Once processed some of your operations may be slower until the cache rebuilds, which could take another 15 minutes. This could result in slow responses or even timeouts for any of your users on slower connections (i.e. Mobile 3G).",
					icon: "warning",
					buttons: true,
					dangerMode: true
				}).then((willDelete) => {
					if (willDelete) {
						$.ajax({
							url: resgrid.absoluteBaseUrl + '/User/Department/ClearDepartmentCache',
							contentType: 'application/json',
							type: 'GET'
						}).done(function (data) {
							swal("Request Sent!", "Your request to have your departments data cache cleared has been sent. Please note it may take up to 15 minutes to clear the data.", "success");
						});
					}
				});
			}
			settings.refreshDepartmentCache = refreshDepartmentCache;
		})(settings = department.settings || (department.settings = {}));
	})(department = resgrid.department || (resgrid.department = {}));
})(resgrid || (resgrid = {}));
