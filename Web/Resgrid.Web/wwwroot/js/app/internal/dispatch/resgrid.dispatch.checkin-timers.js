(function () {
	'use strict';

	var timerStatuses = [];
	var timerInterval = null;

	$(document).ready(function () {
		// callState 0 = Active
		if (typeof callState !== 'undefined' && callState === 0) {
			loadTimerStatuses();
			timerInterval = setInterval(updateCountdowns, 1000);
		}
		loadCheckInHistory();
	});

	function loadTimerStatuses() {
		$.ajax({
			url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCheckInTimerStatuses?callId=' + callId,
			contentType: 'application/json; charset=utf-8',
			type: 'GET'
		}).done(function (result) {
			if (result) {
				timerStatuses = result;
				renderTimerStatuses();
			}
		});
	}

	function renderTimerStatuses() {
		var tbody = $('#checkInTimersBody');
		tbody.empty();

		if (!timerStatuses || timerStatuses.length === 0) {
			tbody.append('<tr><td colspan="4">-</td></tr>');
			return;
		}

		for (var i = 0; i < timerStatuses.length; i++) {
			var t = timerStatuses[i];
			var statusBadge = getStatusBadge(t.Status);
			var remaining = getRemainingTime(t);

			var row = '<tr id="timer-row-' + i + '">' +
				'<td><strong>' + escapeHtml(t.TargetName || t.TargetTypeName) + '</strong><br/><small class="text-muted">' + escapeHtml(t.TargetTypeName) + '</small></td>' +
				'<td class="timer-remaining" data-index="' + i + '">' + remaining + '</td>' +
				'<td class="timer-status" data-index="' + i + '">' + statusBadge + '</td>' +
				'<td><button class="btn btn-xs btn-primary" onclick="showCheckInDialog(' + t.TargetType + ', ' + (t.UnitId || 'null') + ')">Check In</button></td>' +
				'</tr>';
			tbody.append(row);
		}
	}

	function escapeHtml(str) {
		if (!str) return '';
		var div = document.createElement('div');
		div.appendChild(document.createTextNode(str));
		return div.innerHTML;
	}

	function getStatusBadge(status) {
		switch (status) {
			case 'Green':
				return '<span class="label label-primary" style="background-color: #1ab394;">OK</span>';
			case 'Warning':
				return '<span class="label label-warning">Warning</span>';
			case 'Critical':
				return '<span class="label label-danger">OVERDUE</span>';
			default:
				return '<span class="label label-default">' + status + '</span>';
		}
	}

	function getRemainingTime(timer) {
		var remainingMin = timer.DurationMinutes - timer.ElapsedMinutes;
		if (remainingMin <= 0) {
			var overdueMin = Math.abs(remainingMin);
			return '-' + formatMinutes(overdueMin);
		}
		return formatMinutes(remainingMin);
	}

	function formatMinutes(min) {
		var hours = Math.floor(min / 60);
		var mins = Math.floor(min % 60);
		var secs = Math.floor((min * 60) % 60);
		if (hours > 0) return hours + 'h ' + mins + 'm ' + secs + 's';
		return mins + 'm ' + secs + 's';
	}

	function updateCountdowns() {
		for (var i = 0; i < timerStatuses.length; i++) {
			timerStatuses[i].ElapsedMinutes += (1.0 / 60.0);

			var status;
			if (timerStatuses[i].ElapsedMinutes < timerStatuses[i].DurationMinutes)
				status = 'Green';
			else if (timerStatuses[i].ElapsedMinutes < timerStatuses[i].DurationMinutes + timerStatuses[i].WarningThresholdMinutes)
				status = 'Warning';
			else
				status = 'Critical';
			timerStatuses[i].Status = status;

			$('.timer-remaining[data-index="' + i + '"]').text(getRemainingTime(timerStatuses[i]));
			$('.timer-status[data-index="' + i + '"]').html(getStatusBadge(status));
		}
	}

	function loadCheckInHistory() {
		$.ajax({
			url: resgrid.absoluteBaseUrl + '/User/Dispatch/GetCheckInHistory?callId=' + callId,
			contentType: 'application/json; charset=utf-8',
			type: 'GET'
		}).done(function (result) {
			if (result) {
				renderCheckInHistory(result);
			}
		});
	}

	function renderCheckInHistory(records) {
		var tbody = $('#checkInHistoryBody');
		tbody.empty();

		if (!records || records.length === 0) {
			tbody.append('<tr><td colspan="4">-</td></tr>');
			return;
		}

		for (var i = 0; i < records.length; i++) {
			var r = records[i];
			var who = escapeHtml(r.PerformedBy || '');
			var target = escapeHtml(r.CheckInTypeName || '');
			if (r.UnitName) {
				target += ' - ' + escapeHtml(r.UnitName);
			}

			var row = '<tr>' +
				'<td>' + escapeHtml(r.Timestamp) + '</td>' +
				'<td>' + who + '</td>' +
				'<td>' + target + '</td>' +
				'<td>' + escapeHtml(r.Note || '') + '</td>' +
				'</tr>';
			tbody.append(row);
		}
	}

	// Open modal for check-in
	window.showCheckInDialog = function (checkInType, unitId) {
		$('#checkInTargetType').val(checkInType);
		$('#checkInUnitId').val(unitId || '');
		$('#checkInNote').val('');
		$('#checkInModal').modal('show');
	};

	// Wire up modal submit button
	$(document).on('click', '#checkInSubmitBtn', function () {
		var checkInType = parseInt($('#checkInTargetType').val());
		var unitIdVal = $('#checkInUnitId').val();
		var note = $('#checkInNote').val();

		var input = {
			CallId: callId,
			CheckInType: checkInType,
			UnitId: unitIdVal ? parseInt(unitIdVal) : null,
			Note: note || null
		};

		$('#checkInSubmitBtn').prop('disabled', true);

		if (navigator.geolocation) {
			navigator.geolocation.getCurrentPosition(function (pos) {
				input.Latitude = pos.coords.latitude.toString();
				input.Longitude = pos.coords.longitude.toString();
				submitCheckIn(input);
			}, function () {
				submitCheckIn(input);
			}, { timeout: 5000 });
		} else {
			submitCheckIn(input);
		}
	});

	function submitCheckIn(input) {
		$.ajax({
			url: resgrid.absoluteBaseUrl + '/User/Dispatch/PerformCheckIn',
			contentType: 'application/json; charset=utf-8',
			type: 'POST',
			headers: { 'RequestVerificationToken': $('input[name="__RequestVerificationToken"]').val() },
			data: JSON.stringify(input)
		}).done(function (result) {
			$('#checkInModal').modal('hide');
			$('#checkInSubmitBtn').prop('disabled', false);
			if (result && result.Id) {
				loadTimerStatuses();
				loadCheckInHistory();
			}
		}).fail(function () {
			$('#checkInSubmitBtn').prop('disabled', false);
			$('#checkInModal').modal('hide');
			alert('Failed to perform check-in. Please try again.');
		});
	}
})();
