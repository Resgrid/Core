// ADP client-side reveal for server-rendered pages (plan 7.2). The Protected Data Grant
// lives in this closure's MEMORY ONLY — never a cookie, localStorage, sessionStorage, or
// the URL — and every revealed value is concealed again when the step-up window expires,
// the page unloads, or the user conceals manually. Values are inserted with text() so
// decrypted content can never execute as markup.
(function (window, $) {
	'use strict';

	var REDACTED = 'REDACTED';

	var settings = null;   // { verifyUrl, revealUrl, revealData, antiForgeryToken }
	var grantToken = null;
	var expiryTimer = null;
	var revealed = false;

	function fields() {
		return $('[data-adp-field], [data-adp-name]');
	}

	function conceal() {
		if (expiryTimer) {
			window.clearTimeout(expiryTimer);
			expiryTimer = null;
		}
		grantToken = null;

		if (revealed) {
			fields().each(function () {
				var $el = $(this);
				if ($el.data('adp-revealed')) {
					$el.text(REDACTED);
					$el.removeData('adp-revealed');
				}
			});
			revealed = false;
		}

		$('#adpRevealButton').show();
		$('#adpConcealButton').hide();
	}

	function scheduleConceal(expiresOnUtc) {
		var remaining = new Date(expiresOnUtc).getTime() - Date.now();
		if (isNaN(remaining) || remaining <= 0)
			remaining = 1000;
		expiryTimer = window.setTimeout(conceal, remaining);
	}

	function safeValue(values, key) {
		if (!Object.prototype.hasOwnProperty.call(values, key))
			return null;

		var value = values[key];
		if (value === null || value === REDACTED || value === '')
			return null;

		// Never render ciphertext that failed to decrypt server-side.
		if (typeof value === 'string' && (value.indexOf('rgdp:') === 0 || value.indexOf('rgdpb:') === 0))
			return null;

		return value;
	}

	function applyFields(values) {
		fields().each(function () {
			var $el = $(this);

			// Composite display name (contacts): person name, else company name.
			if ($el.is('[data-adp-name]')) {
				var first = safeValue(values, 'contacts.firstname');
				var last = safeValue(values, 'contacts.lastname');
				var company = safeValue(values, 'contacts.companyname');
				var name = $.trim(((first || '') + ' ' + (last || ''))) || company;
				if (name) {
					$el.text(name);
					$el.data('adp-revealed', true);
				}
				return;
			}

			var key = $el.attr('data-adp-field');
			if (!Object.prototype.hasOwnProperty.call(values, key))
				return;

			var value = values[key];
			if (value === null || value === REDACTED || value === '')
				return;

			// Never render ciphertext that failed to decrypt server-side.
			if (typeof value === 'string' && (value.indexOf('rgdp:') === 0 || value.indexOf('rgdpb:') === 0))
				return;

			$el.text(value);
			$el.data('adp-revealed', true);
		});

		revealed = true;
		$('#adpRevealButton').hide();
		$('#adpConcealButton').show();
	}

	function errorText(code) {
		switch (code) {
			case 'invalid_totp': return 'The verification code is invalid or has expired.';
			case 'too_many_attempts': return 'Too many verification attempts. Wait a few minutes and try again.';
			case 'mfa_not_enrolled': return 'Two-factor authentication is not enrolled for this account. Enroll an authenticator app in account security settings first.';
			case 'grants_not_configured': return 'Protected data access is not configured on this server.';
			case 'step_up_required': return 'Verification is required again.';
			case 'grant_expired': return 'The verification window expired. Verify again.';
			case 'grant_revoked': return 'Access was revoked by a policy change. Verify again.';
			case 'protected_access_denied': return 'You are not authorized to view this protected data.';
			case 'broker_unavailable': return 'The protected data service is unavailable. Try again shortly.';
			default: return 'The request failed. Try again.';
		}
	}

	function doReveal() {
		var payload = $.extend({ __RequestVerificationToken: settings.antiForgeryToken }, settings.revealData);
		$.ajax({
			url: settings.revealUrl,
			method: 'POST',
			headers: { 'X-Resgrid-Protected-Grant': grantToken },
			data: payload
		}).done(function (response) {
			if (response && response.success) {
				applyFields(response.fields);
				return;
			}

			var code = response && response.error;
			if (code === 'step_up_required' || code === 'grant_expired' || code === 'grant_revoked') {
				conceal();
				showStepUpModal();
				return;
			}

			window.alert(errorText(code));
		}).fail(function () {
			window.alert(errorText(null));
		});
	}

	function showStepUpModal() {
		$('#adpStepUpError').hide().text('');
		$('#adpStepUpCode').val('');
		$('#adpStepUpModal').modal('show');
	}

	function verify() {
		var code = ($('#adpStepUpCode').val() || '').trim();
		if (!code)
			return;

		$('#adpStepUpSubmit').prop('disabled', true);
		$.post(settings.verifyUrl, {
			__RequestVerificationToken: settings.antiForgeryToken,
			code: code
		}).done(function (response) {
			$('#adpStepUpSubmit').prop('disabled', false);
			if (response && response.success) {
				grantToken = response.grantToken;
				scheduleConceal(response.expiresOnUtc);
				$('#adpStepUpModal').modal('hide');
				doReveal();
				return;
			}

			$('#adpStepUpError').text(errorText(response && response.error)).show();
		}).fail(function () {
			$('#adpStepUpSubmit').prop('disabled', false);
			$('#adpStepUpError').text(errorText(null)).show();
		});
	}

	window.resgridAdpReveal = {
		init: function (options) {
			settings = options;

			$('#adpRevealButton').on('click', function () {
				if (grantToken)
					doReveal();
				else
					showStepUpModal();
			});

			$('#adpConcealButton').on('click', conceal).hide();

			$('#adpStepUpSubmit').on('click', verify);
			$('#adpStepUpCode').on('keypress', function (e) {
				if (e.which === 13) {
					e.preventDefault();
					verify();
				}
			});

			// Belt-and-braces: nothing survives navigation anyway, but drop the token
			// reference the moment the page starts unloading.
			$(window).on('beforeunload', function () {
				grantToken = null;
			});
		}
	};
})(window, jQuery);
