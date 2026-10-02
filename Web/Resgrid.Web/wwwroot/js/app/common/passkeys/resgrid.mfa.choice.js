// Second-factor choices other than a typed code, for Web sign-in and Web step-up (passkey plan sections 7.1, 7.5 rules 2
// and 5, and 7.9): a passkey for the web, and approval from the user's Responder. The server issues every option and
// decides every outcome; this page only runs the browser prompt, shows the approval number on this screen, and goes where
// the server says once it succeeds. Nothing is stored in the browser.
//
//   resgridMfaChoice.init({
//       antiForgeryToken: function () { ... },   // the page's antiforgery token
//       extra: function () { return { returnUrl: ... } },   // posted with every request (optional)
//       passkeyOptionsUrl, verifyPasskeyUrl,       // omit to leave passkeys off
//       requestApprovalUrl, approvalStatusUrl, completeApprovalUrl,   // omit to leave approval off
//       messages: { passkey_failed, passkey_cancelled, passkey_not_supported, approval_waiting, approval_denied,
//                   approval_expired, failed, <server error codes> },
//       navigate: function (url) { ... }           // optional; window.location.assign by default
//   });
//
// Markup: #mfaUsePasskey, #mfaUseResponder, #mfaApprovalPanel, #mfaApprovalNumber, #mfaApprovalStatus, #mfaApprovalCancel,
// #mfaChoiceError.
var resgridMfaChoice = (function () {
	'use strict';

	var APPROVAL_POLL_MS = 2000;
	var settings = null;
	var approvalId = null;
	var approvalTimer = null;
	var busy = false;

	function post(url, data) {
		var body = $.extend({ __RequestVerificationToken: settings.antiForgeryToken() }, settings.extra ? settings.extra() : {}, data || {});
		return $.post(url, body);
	}

	function text(key) {
		var messages = settings.messages || {};
		return messages[key] || messages.failed || '';
	}

	function showError(key) {
		busy = false;
		$('#mfaUsePasskey, #mfaUseResponder').prop('disabled', false);
		$('#mfaChoiceError').text(text(key)).show();
	}

	function clearError() {
		$('#mfaChoiceError').hide().text('');
	}

	function isLocal(url) {
		return typeof url === 'string' && url.charAt(0) === '/' && url.charAt(1) !== '/' && url.charAt(1) !== '\\';
	}

	// A refusal the user can recover from here shows its message; one that ends the sign-in (expired, too many attempts)
	// names where to start again, and the page goes there.
	function failed(response, fallback) {
		if (response && isLocal(response.restart)) {
			settings.navigate(response.restart);
			return;
		}

		showError(response && response.error ? response.error : fallback);
	}

	// The server answers { success: true, redirect } once the sign-in or step-up is complete; the redirect is always a
	// local path it chose, so anything else is refused here too.
	function finished(response) {
		if (response && response.success && isLocal(response.redirect)) {
			settings.navigate(response.redirect);
			return;
		}

		failed(response, 'failed');
	}

	function usePasskey() {
		if (busy)
			return;
		stopApproval();
		clearError();
		if (!window.resgridPasskeys || !window.resgridPasskeys.isSupported()) {
			showError('passkey_not_supported');
			return;
		}

		busy = true;
		$('#mfaUsePasskey').prop('disabled', true);
		post(settings.passkeyOptionsUrl).done(function (start) {
			if (!start || !start.success) {
				failed(start, 'passkey_failed');
				return;
			}

			window.resgridPasskeys.authenticate(start.options).then(function (credential) {
				post(settings.verifyPasskeyUrl, { requestId: start.requestId, credential: JSON.stringify(credential) })
					.done(finished).fail(function () { showError('passkey_failed'); });
			}, function (error) {
				// A closed prompt is the user's choice, never a failed verification.
				var outcome = error && error.outcome;
				showError(outcome === 'cancelled' ? 'passkey_cancelled' : outcome === 'not_supported' ? 'passkey_not_supported' : 'passkey_failed');
			});
		}).fail(function () { showError('passkey_failed'); });
	}

	function stopApproval() {
		if (approvalTimer) {
			window.clearInterval(approvalTimer);
			approvalTimer = null;
		}
		approvalId = null;
		$('#mfaApprovalPanel').hide();
	}

	// Approve with Responder: the number is shown on this screen only, the decision is polled, and an approval is used once.
	function useResponder() {
		if (busy)
			return;
		stopApproval();
		clearError();
		busy = true;
		$('#mfaUseResponder').prop('disabled', true);
		post(settings.requestApprovalUrl).done(function (response) {
			if (!response || !response.success) {
				failed(response, 'failed');
				return;
			}

			busy = false;
			$('#mfaUseResponder').prop('disabled', false);
			approvalId = response.approvalRequestId;
			$('#mfaApprovalNumber').text(response.matchNumber);
			$('#mfaApprovalStatus').text(text('approval_waiting'));
			$('#mfaApprovalPanel').show();
			approvalTimer = window.setInterval(pollApproval, APPROVAL_POLL_MS);
		}).fail(function () { showError('failed'); });
	}

	function pollApproval() {
		var id = approvalId;
		if (!id)
			return;

		post(settings.approvalStatusUrl, { approvalRequestId: id }).done(function (response) {
			if (approvalId !== id)
				return;

			var state = response && response.success ? response.state : null;
			if (state === 'pending')
				return;

			stopApproval();
			if (state === 'approved') {
				busy = true;
				post(settings.completeApprovalUrl, { approvalRequestId: id }).done(finished).fail(function () { showError('failed'); });
				return;
			}

			if (!state) {
				failed(response, 'failed');
				return;
			}

			showError(state === 'denied' ? 'approval_denied' : 'approval_expired');
		});
	}

	function init(options) {
		settings = $.extend({ navigate: function (url) { window.location.assign(url); } }, options);
		busy = false;
		stopApproval();
		$('#mfaUsePasskey').off('click.mfaChoice').on('click.mfaChoice', usePasskey);
		$('#mfaUseResponder').off('click.mfaChoice').on('click.mfaChoice', useResponder);
		$('#mfaApprovalCancel').off('click.mfaChoice').on('click.mfaChoice', function () {
			stopApproval();
			busy = false;
			$('#mfaUsePasskey, #mfaUseResponder').prop('disabled', false);
		});
	}

	return { init: init, stopApproval: stopApproval };
})();
