
var resgrid;
(function (resgrid) {
	var dispatch;
	(function (dispatch) {
		var viewcall;
        (function (viewcall) {
            var noteQuillDescription;
			$(document).ready(function () {
                callMarker = null;
                map = null;

                noteQuillDescription = new Quill('#note-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#newCallForm', function () {
                    $('#Call_Notes').val(noteQuillDescription.root.innerHTML);

                    return true;
                });

				$.ajax({
					url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetMapDataForCall?callId=' + callId,
					contentType: 'application/json; charset=utf-8',
					type: 'GET'
				}).done(function (result) {
                    var data = result;

                    const tiles1 = L.tileLayer(
                        osmTileUrl,
                        {
                            maxZoom: 19,
                            attribution: osmTileAttribution
                        }
                    );

                    map = L.map('callMap', {
                        scrollWheelZoom: false
                    }).setView([data.centerLat, data.centerLon], 11).addLayer(tiles1);

                    callMarker = new L.marker(new L.LatLng(data.centerLat, data.centerLon), { draggable: false }).addTo(map);
				});

				$('.callImages').slick({
					infinite: true,
					speed: 500,
					adaptiveHeight: true
				});

				$("#note-box").keyup(function (event) {
					if (event.keyCode === 13) {
						$("#note-box-submit").click();
					}
				});

				$("#note-box1").keyup(function (event) {
					if (event.keyCode === 13) {
						$("#note-box-submit1").click();
					}
				});

				viewcall.player = new Plyr('#player');

				resgrid.common.signalr.init(notifyCallUpdated, null, null, null);
				viewcall.getCallNotes(false);

				$('#protocolTextWindow').on('show.bs.modal', function (event) {
					var protocolId = $(event.relatedTarget).data('protocolid');

					var modal = $(this);

					$.ajax({
						url: resgrid.absoluteBaseUrl + '/User/Protocols/GetTextForProtocol?id=' + protocolId,
						contentType: 'application/json; charset=utf-8',
						type: 'GET'
					}).done(function (result) {
						if (result) {
							modal.find('.modal-title').text(`Protocol Text for ${result.Name}`);
							modal.find('.modal-body').empty();
							modal.find('.modal-body').append(result.Text);
						}
					});
				});
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
					getCallNotes(false);
				});
			}
			viewcall.addCallNote = addCallNote;
			function addCallNote1() {
				$.ajax({
					url: resgrid.absoluteBaseUrl + '/User/Dispatch/AddCallNote',
					data: JSON.stringify({
						CallId: callId,
						//Note: $('#note-box1').val()
                        Note: noteQuillDescription.root.innerHTML
					}),
					contentType: 'application/json',
					type: 'POST'
				}).done(function (data) {
					//$('#note-box1').val('');
                    noteQuillDescription.root.innerHTML = '';
					getCallNotes(true);
				});
			}
			viewcall.addCallNote1 = addCallNote1;
			function getCallNotes(fromSubmitButton) {
				$.ajax({
					url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCallNotes?callId=' + callId,
					contentType: 'application/json; charset=utf-8',
					type: 'GET'
				}).done(function (result) {
					var notesHtml = $('#note-messages-inner');
					var notesHtml1 = $('#note-messages-inner1');
					if (result) {
						notesHtml.empty();
						notesHtml1.empty();
                        for (var i = 0; i < result.length; i++) {

                            let flagHtml = '';
                            if (result[i].IsFlagged) {
                                flagHtml = "<a href='" + resgrid.absoluteBaseUrl + '/User/Dispatch/FlagCallNote?callId=' + callId + "&callNoteId=" + result[i].CallNoteId + "'> <i class='fa fa-flag' style='color: #ff0000;'></i> </a>";
                            } else {
                                flagHtml = "<a href='" + resgrid.absoluteBaseUrl + '/User/Dispatch/FlagCallNote?callId=' + callId + "&callNoteId=" + result[i].CallNoteId + "'> <i class='fa fa-flag'></i> </a>";
                            }

                            notesHtml.append("<div class='feed-element'><a href='#' class='pull-left'><img alt='image' class='img-circle' src='" + resgrid.absoluteApiBaseUrl + "/api/v3/Avatars/Get?id=" + result[i].UserId + "' onerror=\"this.src='" + resgrid.absoluteBaseUrl + "/images/defaultProfile.png\'\"></a><div class='media-body'><small class='pull-right'>" + result[i].Location + "</small><strong>" + result[i].Name + "</strong><br><small class='text-muted'>" + result[i].Timestamp + "</small><div class='well'>" + result[i].Note + "</div></div></div>");
                            notesHtml1.append("<div class='feed-element'><a href='#' class='pull-left'><img alt='image' class='img-circle' src='" + resgrid.absoluteApiBaseUrl + "/api/v3/Avatars/Get?id=" + result[i].UserId + "' onerror=\"this.src='" + resgrid.absoluteBaseUrl + "/images/defaultProfile.png\'\"></a><div class='media-body'><small class='pull-right'>" + result[i].Location + "</small><strong>" + result[i].Name + "</strong><br><small class='text-muted'>" + result[i].Timestamp + "</small><div class='well pull-left' style='width:95%'>" + result[i].Note + "</div><div class='pull-right' style='width=4%'>" + flagHtml + "</div></div></div>");
						}

						if (fromSubmitButton) {
							$('html, body').animate({
								scrollTop: $(document).height() - $(window).height()
							},
								1400,
								"easeOutQuint"
							);
						} else {
							$('#note-messages-inner').animate({
								scrollTop: $('#note-messages-inner')[0].scrollHeight
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
					text: "Are you sure you want to re-open this call? This was delete all the associated close data (i.e. who closed the call, when, it's close state and any notes on the close).",
					icon: "warning",
					buttons: true,
					dangerMode: true
				}).then((willDelete) => {
					if (willDelete) {
						$.ajax({
							url: resgrid.absoluteBaseUrl + '/User/Dispatch/ReOpenCall?callId=' + callId,
							contentType: 'application/json',
							type: 'GET'
						}).done(function (data) {
							swal("Request Sent!", "Your request to re-open the call is being processed. Please check the Open Calls (Disaptch) dashboard page to now view the call.", "success");
						});
					}
				});
			}
			viewcall.reOpenCall = reOpenCall;
		})(viewcall = dispatch.viewcall || (dispatch.viewcall = {}));
	})(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
