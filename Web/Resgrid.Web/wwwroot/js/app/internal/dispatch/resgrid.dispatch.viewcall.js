
var resgrid;
(function (resgrid) {
	var dispatch;
	(function (dispatch) {
		var viewcall;
        (function (viewcall) {
            var noteQuillDescription;
            function getText(key, fallback) {
                return (resgrid.dispatch && typeof resgrid.dispatch.getText === 'function')
                    ? resgrid.dispatch.getText(key, fallback)
                    : fallback;
            }
            function formatText(template) {
                if (resgrid.dispatch && typeof resgrid.dispatch.formatText === 'function') {
                    return resgrid.dispatch.formatText.apply(null, arguments);
                }
                var args = Array.prototype.slice.call(arguments, 1);
                return (template || '').replace(/\{(\d+)\}/g, function (match, index) {
                    return typeof args[index] !== 'undefined' ? args[index] : match;
                });
            }
			$(document).ready(function () {
                callMarker = null;
                map = null;

                // The note editor is rendered only for members Add Call Data allows (ViewCallView.CanAddCallData).
                if ($('#note-container').length) {
                    noteQuillDescription = new Quill('#note-container', {
                        placeholder: '',
                        theme: 'snow'
                    });
                    // addCallNote1 reads noteQuillDescription, so its button is enabled only once the
                    // editor exists. Bound here rather than inline (RESGRID-WEB-1MA).
                    $('#note-box-submit1').on('click', addCallNote1).prop('disabled', false);
                }

                $(document).on('submit', '#newCallForm', function () {
                    if (noteQuillDescription)
                        $('#Call_Notes').val(noteQuillDescription.root.innerHTML);

                    return true;
                });

				$.ajax({
					url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetMapDataForCall?callId=' + callId,
					contentType: 'application/json; charset=utf-8',
					type: 'GET'
				}).done(function (result) {
                    var data = result;
                    var center = [Number(data.centerLat), Number(data.centerLon)];

                    resgrid.maps.create('callMap', { center: center, zoom: 11, scrollZoom: false, traffic: 'off' }).then(function (created) {
                        map = created;
                        callMarker = map.addMarker(center);

                        // The map tab can start hidden (Dispatched tab active), which leaves the map sized 0x0.
                        $('a[href="#tab-1"]').on('shown.bs.tab', function () {
                            map.resize();
                            map.setView(center, map.getZoom());
                        });
                    });
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
							modal.find('.modal-title').text(formatText(getText('protocolTextFor', 'Protocol Text for {0}'), result.Name));
							modal.find('.modal-body').empty();
							modal.find('.modal-body').append(result.Text);
						}
					});
				});
			});
			function notifyCallUpdated(id) {
				if (id == callId) {
					resgrid.common.notifications.showInformational(
                        getText('callUpdatedTitle', 'Call Updated'),
                        getText('callUpdatedMessage', 'This call was updated, please refresh the page to view the latest information.'));
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
					type: 'POST',
					// JSON bodies cannot carry the antiforgery field, so the token travels as a header.
					headers: { 'RequestVerificationToken': $('#callNoteTokenForm input[name="__RequestVerificationToken"]').val() }
				}).done(function (data) {
					$('#note-box').val('');
					getCallNotes(false);
				});
			}
			viewcall.addCallNote = addCallNote;
			// Bound here rather than inline: the button renders disabled until this script has
			// run, so an early click can't call into an undefined namespace (RESGRID-WEB-1MA).
			$('#note-box-submit').on('click', addCallNote).prop('disabled', false);
			function addCallNote1() {
				$.ajax({
					url: resgrid.absoluteBaseUrl + '/User/Dispatch/AddCallNote',
					data: JSON.stringify({
						CallId: callId,
						//Note: $('#note-box1').val()
                        Note: noteQuillDescription.root.innerHTML
					}),
					contentType: 'application/json',
					type: 'POST',
					headers: { 'RequestVerificationToken': $('#callNoteTokenForm input[name="__RequestVerificationToken"]').val() }
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

                            notesHtml.append("<div class='feed-element'><a href='#' class='pull-left'><img alt='image' class='img-circle' src='" + resgrid.absoluteApiBaseUrl + "/api/v4/Avatars/Get?id=" + result[i].UserId + "' onerror=\"this.src='" + resgrid.absoluteBaseUrl + "/images/defaultProfile.png\'\"></a><div class='media-body'><small class='pull-right'>" + resgrid.escapeHtml(result[i].Location) + "</small><strong>" + resgrid.escapeHtml(result[i].Name) + "</strong><br><small class='text-muted'>" + resgrid.escapeHtml(result[i].Timestamp) + "</small><div class='well'>" + result[i].Note + "</div></div></div>");
                            notesHtml1.append("<div class='feed-element'><a href='#' class='pull-left'><img alt='image' class='img-circle' src='" + resgrid.absoluteApiBaseUrl + "/api/v4/Avatars/Get?id=" + result[i].UserId + "' onerror=\"this.src='" + resgrid.absoluteBaseUrl + "/images/defaultProfile.png\'\"></a><div class='media-body'><small class='pull-right'>" + resgrid.escapeHtml(result[i].Location) + "</small><strong>" + resgrid.escapeHtml(result[i].Name) + "</strong><br><small class='text-muted'>" + resgrid.escapeHtml(result[i].Timestamp) + "</small><div class='well pull-left' style='width:95%'>" + result[i].Note + "</div><div class='pull-right' style='width=4%'>" + flagHtml + "</div></div></div>");
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
					title: getText('areYouSure', 'Are you sure?'),
					text: getText('reopenCallConfirmText', 'Are you sure you want to re-open this call? This will delete all the associated close data, including who closed the call, when it was closed, its close state, and any close notes.'),
					icon: "warning",
					buttons: true,
					dangerMode: true
				}).then((willDelete) => {
					if (willDelete) {
						// ReOpenCall is POST + antiforgery (the page's #reOpenCallForm token).
						$.ajax({
							url: resgrid.absoluteBaseUrl + '/User/Dispatch/ReOpenCall',
							type: 'POST',
							data: {
								callId: callId,
								__RequestVerificationToken: $('#reOpenCallForm input[name="__RequestVerificationToken"]').val()
							}
						}).done(function (data) {
							swal(
                                getText('requestSent', 'Request Sent!'),
                                getText('reopenCallRequestSentMessage', 'Your request to re-open the call is being processed. Please check the Open Calls dashboard page to view the call.'),
                                "success");
						});
					}
				});
			}
			viewcall.reOpenCall = reOpenCall;
			// Bound here rather than inline (RESGRID-WEB-1MA); the button renders disabled until now.
			$('#reOpenCallButton').on('click', reOpenCall).prop('disabled', false);

			// Release (stand down) one unit or member from the open call: the Release buttons (_ViewCallResourceRelease). The
			// strings come localized and attribute-encoded from the button; they are set as text here, never as HTML.
			function releaseResource(button) {
				var btn = $(button);
				var isUnit = btn.attr('data-kind') === 'unit';
				var content = document.createElement('div');
				var text = document.createElement('p');
				text.textContent = btn.attr('data-text') || '';
				content.appendChild(text);
				var label = document.createElement('label');
				var notify = document.createElement('input');
				notify.type = 'checkbox';
				notify.checked = true;
				notify.style.marginRight = '6px';
				label.appendChild(notify);
				label.appendChild(document.createTextNode(btn.attr('data-notify-label') || ''));
				content.appendChild(label);

				swal({
					title: btn.attr('data-title') || '',
					content: content,
					icon: 'warning',
					buttons: true,
					dangerMode: true
				}).then(function (confirmed) {
					if (!confirmed)
						return;

					var failed = btn.attr('data-failed') || '';
					btn.prop('disabled', true);
					$.ajax({
						url: resgrid.absoluteBaseUrl + (isUnit ? '/User/Dispatch/ReleaseUnitFromCall' : '/User/Dispatch/ReleasePersonnelFromCall'),
						type: 'POST',
						data: {
							callId: callId,
							unitId: isUnit ? btn.attr('data-resource-id') : undefined,
							userId: isUnit ? undefined : btn.attr('data-resource-id'),
							notify: notify.checked,
							__RequestVerificationToken: $('#releaseUnitForm input[name="__RequestVerificationToken"]').val()
						}
					}).done(function (data) {
						// Already released (outcome 4): a retry after a lost response, or another dispatcher got there first.
						// Either way it is off the call, so show the page as it now is.
						if (data && (data.success || data.outcome === 4)) {
							window.location.reload();
						} else {
							btn.prop('disabled', false);
							swal(failed, '', 'error');
						}
					}).fail(function () {
						btn.prop('disabled', false);
						swal(failed, '', 'error');
					});
				});
			}
			viewcall.releaseResource = releaseResource;
			$(document).on('click', '.release-resource-button', function () {
				releaseResource(this);
			});
		})(viewcall = dispatch.viewcall || (dispatch.viewcall = {}));
	})(dispatch = resgrid.dispatch || (resgrid.dispatch = {}));
})(resgrid || (resgrid = {}));
