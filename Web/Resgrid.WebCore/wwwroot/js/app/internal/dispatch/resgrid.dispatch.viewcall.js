
var resgrid;
(function (resgrid) {
	var dispatch;
	(function (dispatch) {
		var viewcall;
		(function (viewcall) {
			$(document).ready(function () {
				marker = null;
				$.ajax({
					url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetMapDataForCall?callId=' + callId,
					contentType: 'application/json; charset=utf-8',
					type: 'GET'
				}).done(function (result) {
					var data = result;
					var mapCenter = new google.maps.LatLng(data.centerLat, data.centerLon);
					var mapOptions = {
						zoom: 10,
						center: mapCenter
					};
					map = new google.maps.Map(document.getElementById('callMap'), mapOptions);
					marker = new google.maps.Marker({
						position: mapCenter,
						map: map,
						title: 'Call Location',
						animation: google.maps.Animation.DROP,
						draggable: true,
						bounds: false
					});
				});
				getCallNotes();

				$('.callImages').slick({
					infinite: true,
					speed: 500,
					adaptiveHeight: true
				});

				resgrid.common.signalr.init(notifyCallUpdated, null, null, null);
			});
			function notifyCallUpdated(id) {
				if (id == callId) {
					resgrid.common.notifications.showInformational("Call Updated", "This call was updated, please refresh the page to view the latest information.");
				}
			}
			viewcall.notifyCallUpdated = notifyCallUpdated;
			function addCallNote() {
				$.ajax({
					url: resgrid.absoluteBaseUrl + '/User/Dispatch/AddCallNote',
					data: JSON.stringify({
						CallId: callId,
						Note: $('#note-box').val()
					}),
					contentType: 'application/json',
					type: 'POST'
				}).done(function (data) {
					$('#note-box').val('');
                    getCallNotes(true);
				});
			}
			viewcall.addCallNote = addCallNote;
			function getCallNotes(fromSubmitButton) {
				$.ajax({
					url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCallNotes?callId=' + callId,
					contentType: 'application/json; charset=utf-8',
					type: 'GET'
				}).done(function (result) {
					var notesHtml = $('#note-messages-inner');
					if (result) {
						notesHtml.empty();
						for (var i = 0; i < result.length; i++) {
							notesHtml.append("<div class='feed-element'><a href='#' class='pull-left'><img alt='image' class='img-circle' src='https://api.resgrid.com/api/v3/Avatars/Get?id=" + result[i].UserId + "' onerror=\"this.src='https://resgrid.com/Images/defaultProfile.png\'\"></a><div class='media-body'><small class='pull-right'>" + result[i].Location + "</small><strong>" + result[i].Name + "</strong><br><small class='text-muted'>" + result[i].Timestamp + "</small><div class='well'>" + result[i].Note + "</div></div></div>");
                        }

                        if (fromSubmitButton) {
                            $('html, body').animate({
                                scrollTop: $(document).height() - $(window).height()
                            },
                                1400,
                                "easeOutQuint"
                            );
                        }
					}
				});
			}
			viewcall.getCallNotes = getCallNotes;
			function reOpenCall() {
				swal({
					title: "Are you sure?",
					text: "Are you sure you want to re-open this call? This was delete all the assoicated close data (i.e. who closed the call, when, it's close state and any notes on the close).",
					type: "warning",
					showCancelButton: true,
					confirmButtonColor: "#DD6B55",
					confirmButtonText: "Yes, re-open the call",
					closeOnConfirm: false
				},
					function () {
						$.ajax({
							url: resgrid.absoluteBaseUrl + '/User/Dispatch/ReOpenCall?callId=' + callId,
							contentType: 'application/json',
							type: 'GET'
						}).done(function (data) {
							swal("Request Sent!", "Your request to re-open the call is being processed. Please check the Open Calls (Disaptch) dashboard page to now view the call.", "success");
						});
					}
				);
			}
			viewcall.reOpenCall = reOpenCall;
		})(viewcall = dispatch.viewcall || (dispatch.viewcall = {}));
	})(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
