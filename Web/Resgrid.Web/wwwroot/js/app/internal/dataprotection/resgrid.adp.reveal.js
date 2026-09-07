// ADP client-side reveal for server-rendered pages (plan 7.2). The Protected Data Grant
// lives in this closure's MEMORY ONLY — never a cookie, localStorage, sessionStorage, or
// the URL — and every revealed value is concealed again when the step-up window expires,
// the page unloads, or the user conceals manually. Values are inserted with text() so
// decrypted content can never execute as markup.
//
// Two additions cover the pages where a person can lose work (edit forms whose revealed
// fields would be wiped at expiry, and the Records authoring pages whose save needs the
// grant): an expiry warning that lets the user re-verify IN PLACE before the window closes,
// and bound forms (bindForm) whose submit is held until a live grant can travel with it.
(function (window, $) {
	'use strict';

	var REDACTED = 'REDACTED';
	var GRANT_FIELD = '__ResgridProtectedGrant';
	var EXPIRES_FIELD = '__ResgridProtectedGrantExpiresOn';
	var DEFAULT_WARN_SECONDS = 120;

	var settings = null;      // { verifyUrl, requestGrantUrl, revealUrl, revealData, antiForgeryToken, messages, onRevealed, onConcealed, onRenewed, grantExpiresOnUtc, bindForms, warnBeforeSeconds }
	var grantToken = null;
	var serverGrant = false;  // the page arrived holding a grant in a bound form's hidden field (the *Revealed actions)
	var expiresAt = null;     // epoch milliseconds when known
	var expiryTimer = null;
	var warnTimer = null;
	var tickTimer = null;
	var revealed = false;
	var boundForms = [];
	var pendingAction = null; // what to do once a grant is acquired; the reveal when nothing else is waiting
	var pendingForm = null;
	var passThrough = null;   // the form whose submit is being re-dispatched with the grant attached

	function fields() {
		return $('[data-adp-field], [data-adp-name]');
	}

	function clearTimers() {
		if (expiryTimer) {
			window.clearTimeout(expiryTimer);
			expiryTimer = null;
		}
		if (warnTimer) {
			window.clearTimeout(warnTimer);
			warnTimer = null;
		}
		stopTick();
	}

	function stopTick() {
		if (tickTimer) {
			window.clearInterval(tickTimer);
			tickTimer = null;
		}
	}

	function hasLiveGrant() {
		if (!grantToken && !serverGrant)
			return false;

		return expiresAt === null || Date.now() < expiresAt;
	}

	function conceal() {
		clearTimers();
		grantToken = null;
		serverGrant = false;
		expiresAt = null;

		var wasRevealed = revealed;

		if (revealed) {
			fields().each(function () {
				var $el = $(this);
				if ($el.data('adp-revealed')) {
					write($el, REDACTED);
					$el.removeData('adp-revealed');
				}
			});
			revealed = false;
		}

		// A bound form no longer carries a token the server would refuse; its next submit asks again.
		$.each(boundForms, function (_, form) {
			clearGrantFields(form);
		});

		$('#adpRevealButton').show();
		$('#adpConcealButton').hide();

		// Host hook: a page whose values are fetched by another module (the profile page's
		// emergency contacts) reloads them WITHOUT the grant so plaintext leaves the DOM at the
		// same moment the marked-up fields do.
		if (wasRevealed && settings && typeof settings.onConcealed === 'function')
			settings.onConcealed();
	}

	// The window closed without a renewal. Revealed values leave the DOM as before; a page with a
	// bound form keeps the user's typed work and tells them to re-verify before saving.
	function onExpired() {
		var hadForms = boundForms.length > 0;
		conceal();
		if (hadForms)
			showWarning(true);
		else
			hideWarning();
	}

	function warnSeconds() {
		var value = settings && Number(settings.warnBeforeSeconds);
		return value > 0 ? value : DEFAULT_WARN_SECONDS;
	}

	function scheduleTimers(expiresOnUtc) {
		clearTimers();

		var at = new Date(expiresOnUtc).getTime();
		if (isNaN(at)) {
			expiresAt = null;
			return;
		}

		expiresAt = at;
		var remaining = at - Date.now();
		if (remaining <= 0)
			remaining = 1000;

		expiryTimer = window.setTimeout(onExpired, remaining);
		warnTimer = window.setTimeout(function () {
			showWarning(false);
		}, Math.max(0, remaining - warnSeconds() * 1000));
	}

	function formatRemaining(milliseconds) {
		var seconds = Math.max(0, Math.ceil(milliseconds / 1000));
		var minutes = Math.floor(seconds / 60);
		var rest = seconds % 60;
		return minutes + ':' + (rest < 10 ? '0' : '') + rest;
	}

	// The warning is a fixed toast built here rather than by each host view, so every page that
	// initialises the module gets it. Text always goes through text(): never markup.
	function warningElement() {
		var $el = $('#adpExpiryWarning');
		if ($el.length)
			return $el;

		$el = $('<div id="adpExpiryWarning" class="alert alert-warning" role="alert" aria-live="polite"></div>')
			.css({ position: 'fixed', top: '70px', right: '20px', zIndex: 1060, maxWidth: '380px', boxShadow: '0 2px 8px rgba(0,0,0,0.25)', display: 'none' });
		$el.append(
			$('<i class="fa fa-shield"></i>'), ' ',
			$('<span id="adpExpiryText"></span>'), ' ',
			$('<button type="button" class="btn btn-primary btn-xs" id="adpRenewButton"></button>').text(messageText('renew')).on('click', renew), ' ',
			$('<button type="button" class="btn btn-default btn-xs" id="adpExpiryDismiss" aria-label="Dismiss">&times;</button>').on('click', hideWarning));
		$('body').append($el);
		return $el;
	}

	function showWarning(expired) {
		var $el = warningElement();
		var $text = $el.find('#adpExpiryText');
		stopTick();

		if (expired) {
			$text.text(messageText('expired'));
			$el.show();
			return;
		}

		function tick() {
			var remaining = expiresAt === null ? 0 : expiresAt - Date.now();
			$text.text(messageText('expiring').replace('{0}', formatRemaining(remaining)));
		}

		tick();
		tickTimer = window.setInterval(tick, 1000);
		$el.show();
	}

	function hideWarning() {
		stopTick();
		$('#adpExpiryWarning').hide();
	}

	function ensureField(form, name) {
		var input = form.querySelector('input[name="' + name + '"]');
		if (!input) {
			input = window.document.createElement('input');
			input.type = 'hidden';
			input.name = name;
			form.appendChild(input);
		}
		return input;
	}

	// Writes the grant into the form's hidden fields. A page that arrived holding a server-issued
	// grant (grantToken null, serverGrant true) keeps the field it was rendered with.
	function writeGrantFields(form) {
		if (!form)
			return;
		if (grantToken)
			ensureField(form, GRANT_FIELD).value = grantToken;
		ensureField(form, EXPIRES_FIELD).value = expiresAt === null ? '' : new Date(expiresAt).toISOString();
	}

	function clearGrantFields(form) {
		var grant = form && form.querySelector('input[name="' + GRANT_FIELD + '"]');
		if (grant)
			grant.value = '';
		var expires = form && form.querySelector('input[name="' + EXPIRES_FIELD + '"]');
		if (expires)
			expires.value = '';
	}

	// A grant was just issued (first reveal or renewal). Bound forms get it straight away so a save
	// that follows carries it, and the host is told so paused work (autosave) can resume.
	function setGrant(token, expiresOnUtc) {
		grantToken = token;
		serverGrant = false;
		if (expiresOnUtc)
			scheduleTimers(expiresOnUtc);
		else {
			clearTimers();
			expiresAt = null;
		}
		hideWarning();

		$.each(boundForms, function (_, form) {
			writeGrantFields(form);
			form.dispatchEvent(new window.CustomEvent('adp:grant-renewed'));
		});

		if (settings && typeof settings.onRenewed === 'function')
			settings.onRenewed();
	}

	function grantAcquired(token, expiresOnUtc) {
		setGrant(token, expiresOnUtc);

		var action = pendingAction || doReveal;
		pendingAction = null;
		pendingForm = null;
		action();
	}

	// Runs the step-up flow (exempt-app grant first, then the prompt) and performs the action once
	// a grant is live. Cancelling the prompt drops the action and tells the waiting form.
	function acquire(action, form) {
		// A second request displaces the first: the reveal button stays live while a bound form waits for its
		// grant. Tell the displaced form so it is not left silently waiting for a submit that never comes.
		if (pendingForm && pendingForm !== form)
			pendingForm.dispatchEvent(new window.CustomEvent('adp:submit-cancelled'));

		pendingAction = action;
		pendingForm = form || null;
		requestGrantWithoutStepUp();
	}

	function renew() {
		acquire(function () {
			// Values wiped at expiry come back through the page's own reveal endpoint; a page without
			// one (the Records edit pages) only needed the grant itself.
			if (!revealed && settings && settings.revealUrl)
				doReveal();
		}, null);
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

	// A revealed value can land on a read-only element or on a form control. Text nodes take
	// text() so decrypted content can never execute as markup; inputs take val(), which is not a
	// markup context at all. A wrapper (the UDF renderer marks the form-group, not each input
	// variant) hands the value to the control inside it.
	function write($el, value) {
		if ($el.is('input, textarea, select')) {
			$el.val(value);
			return;
		}

		var $control = $el.find('input, textarea, select').first();
		if ($control.length) {
			$control.val(value);
			return;
		}

		$el.text(value);
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

			write($el, value);
			$el.data('adp-revealed', true);
		});

		revealed = true;
		$('#adpRevealButton').hide();
		$('#adpConcealButton').show();

		// Host hook: see conceal(). Runs after the marked fields are written so a reloading
		// module can assume the grant is live. The values are handed over as well, for the fields a
		// data-adp-field marker cannot reach on its own - a coordinate pair split across two inputs,
		// or a rich-text editor whose DOM is owned by another library.
		if (settings && typeof settings.onRevealed === 'function')
			settings.onRevealed(values);
	}

	// English fallbacks: the host view supplies localized text through settings.messages, keyed by
	// the same value-free reason codes the server returns. A missing key still renders something
	// readable rather than an empty alert.
	var DEFAULT_MESSAGES = {
		invalid_totp: 'The verification code is invalid or has expired.',
		too_many_attempts: 'Too many verification attempts. Wait a few minutes and try again.',
		mfa_not_enrolled: 'Two-factor authentication is not enrolled for this account. Enroll an authenticator app in account security settings first.',
		grants_not_configured: 'Protected data access is not configured on this server.',
		step_up_required: 'Verification is required again.',
		grant_expired: 'The verification window expired. Verify again.',
		grant_revoked: 'Access was revoked by a policy change. Verify again.',
		protected_access_denied: 'You are not authorized to view this protected data.',
		broker_unavailable: 'The protected data service is unavailable. Try again shortly.',
		generic: 'The request failed. Try again.',
		expiring: 'Your verification expires in {0}. Re-verify now to keep working without losing changes.',
		expired: 'Your verification has expired. Re-verify to continue; unsaved changes stay on this page until you do.',
		renew: 'Re-verify'
	};

	function messageText(key) {
		var messages = (settings && settings.messages) || {};
		return messages[key] || DEFAULT_MESSAGES[key];
	}

	function errorText(code) {
		var key = code && Object.prototype.hasOwnProperty.call(DEFAULT_MESSAGES, code) ? code : 'generic';
		return messageText(key);
	}

	function doReveal() {
		// A page whose values are fetched by something else (the moderation queue reads the v4 API
		// through its own component) has no per-record reveal endpoint. Step-up alone is the whole
		// job: the grant is now live, so the host reloads and the same requests come back revealed.
		if (!settings.revealUrl) {
			revealed = true;
			$('#adpRevealButton').hide();
			$('#adpConcealButton').show();

			if (typeof settings.onRevealed === 'function')
				settings.onRevealed();

			return;
		}

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

	// A department may release named apps from the step-up prompt (plan 3.3) - a dispatcher on a
	// live incident cannot stop to read a code off a phone. Ask the server first: it answers with a
	// grant when this department has exempted this app, and with step_up_required otherwise, which
	// is what puts the prompt back. The client never decides this; it only asks.
	//
	// Any failure falls through to the prompt. Erring towards asking for a second factor is the
	// direction that cannot cause harm.
	function requestGrantWithoutStepUp() {
		if (!settings.requestGrantUrl) {
			showStepUpModal();
			return;
		}

		$.post(settings.requestGrantUrl, { __RequestVerificationToken: settings.antiForgeryToken })
			.done(function (response) {
				if (response && response.success && response.grantToken) {
					grantAcquired(response.grantToken, response.expiresOnUtc);
					return;
				}

				showStepUpModal();
			})
			.fail(function () {
				showStepUpModal();
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
				// The waiting action runs before the modal closes so a cancel handler never sees it.
				grantAcquired(response.grantToken, response.expiresOnUtc);
				$('#adpStepUpModal').modal('hide');
				return;
			}

			$('#adpStepUpError').text(errorText(response && response.error)).show();
		}).fail(function () {
			$('#adpStepUpSubmit').prop('disabled', false);
			$('#adpStepUpError').text(errorText(null)).show();
		});
	}

	// Downloads a protected binary payload (a certification document, an attachment) with the
	// grant on the request. A plain <a href> cannot carry the grant header, so without this a
	// protected file would be unreachable from a server-rendered page — the encryption would have
	// made the member's own document permanently undownloadable rather than merely concealed.
	// The blob is revoked immediately after the save so decrypted bytes do not linger.
	function downloadProtected(url, fileName, onError) {
		if (!grantToken) {
			if (onError)
				onError(errorText('step_up_required'));
			return;
		}

		var headers = new window.Headers();
		headers.append('X-Resgrid-Protected-Grant', grantToken);

		window.fetch(url, { headers: headers, credentials: 'same-origin' })
			.then(function (response) {
				if (!response.ok)
					throw new Error(response.status === 403 || response.status === 404
						? 'protected_access_denied'
						: 'generic');

				return response.blob();
			})
			.then(function (blob) {
				var objectUrl = window.URL.createObjectURL(blob);
				var link = window.document.createElement('a');
				link.href = objectUrl;
				link.download = fileName || 'download';
				window.document.body.appendChild(link);
				link.click();
				window.document.body.removeChild(link);
				window.URL.revokeObjectURL(objectUrl);
			})
			.catch(function (error) {
				if (onError)
					onError(errorText(error && error.message ? error.message : 'generic'));
			});
	}

	// jQuery beforeSend hook for a host module that fetches its own protected values (the
	// profile page's emergency contacts come from their own endpoint, which already reads this
	// header). The token is written onto the request and never returned to the caller, so it
	// stays inside this closure.
	function applyGrantHeader(target) {
		if (!grantToken || !target)
			return;

		// Works for a jQuery/XMLHttpRequest (setRequestHeader) and for a fetch Headers object
		// (set). Either way the token is written INTO the caller's request and never returned,
		// so it stays inside this closure.
		if (typeof target.setRequestHeader === 'function')
			target.setRequestHeader('X-Resgrid-Protected-Grant', grantToken);
		else if (typeof target.set === 'function')
			target.set('X-Resgrid-Protected-Grant', grantToken);
	}

	// A full-page form post cannot carry a header. The Records and incident-report edit pages hand
	// the grant to the server through a hidden field written at the moment the form is submitted, so
	// the token leaves this closure only for that one request (RMS plan section 5.9.3). The server
	// re-renders the page revealed and carries the same field through the edit form's own posts.
	function submitWithGrant(form) {
		if (!grantToken || !form || typeof form.submit !== 'function')
			return false;

		writeGrantFields(form);
		form.submit();
		return true;
	}

	// Holds a form's submit until a live grant can travel with it. A submit with a live grant goes
	// through with the hidden fields written; without one the step-up runs in place and the same
	// submit (same button, same values) is re-dispatched afterwards, so nothing typed is lost.
	// Cancelling the prompt leaves the form as it was and raises adp:submit-cancelled on it.
	function bindForm(target) {
		var form = typeof target === 'string' ? window.document.querySelector(target) : target;
		if (!form || boundForms.indexOf(form) >= 0)
			return;

		boundForms.push(form);

		form.addEventListener('submit', function (e) {
			if (passThrough === form) {
				passThrough = null;
				return;
			}
			if (e.defaultPrevented)
				return;

			if (hasLiveGrant()) {
				writeGrantFields(form);
				return;
			}

			e.preventDefault();
			var submitter = e.submitter || null;
			acquire(function () {
				writeGrantFields(form);
				passThrough = form;
				if (typeof form.requestSubmit === 'function')
					form.requestSubmit(submitter);
				else
					form.submit();
			}, form);
		});

		// The host's own save path (autosave) learned from the server that the grant is gone.
		form.addEventListener('adp:grant-required', function () {
			clearTimers();
			grantToken = null;
			serverGrant = false;
			expiresAt = null;
			clearGrantFields(form);
			showWarning(true);
		});
	}

	window.resgridAdpReveal = {
		download: downloadProtected,
		applyGrantHeader: applyGrantHeader,
		submitWithGrant: submitWithGrant,
		bindForm: bindForm,
		renew: renew,
		hasLiveGrant: hasLiveGrant,

		init: function (options) {
			settings = options || {};

			// A page rendered by a *Revealed action already carries its grant in the bound form's
			// hidden field; only the expiry is known here, for the warning and the submit hold.
			if (settings.grantExpiresOnUtc) {
				serverGrant = true;
				scheduleTimers(settings.grantExpiresOnUtc);
			}

			$.each(settings.bindForms || [], function (_, form) {
				bindForm(form);
			});

			$('#adpRevealButton').on('click', function () {
				if (hasLiveGrant() && grantToken) {
					doReveal();
					return;
				}

				acquire(doReveal, null);
			});

			$('#adpConcealButton').on('click', conceal).hide();

			$('#adpStepUpSubmit').on('click', verify);
			$('#adpStepUpCode').on('keypress', function (e) {
				if (e.which === 13) {
					e.preventDefault();
					verify();
				}
			});

			// Closing the prompt without a code abandons whatever was waiting on the grant.
			$('#adpStepUpModal').on('hidden.bs.modal', function () {
				if (!pendingAction)
					return;
				var form = pendingForm;
				pendingAction = null;
				pendingForm = null;
				if (form)
					form.dispatchEvent(new window.CustomEvent('adp:submit-cancelled'));
			});

			// Belt-and-braces: nothing survives navigation anyway, but drop the token
			// reference the moment the page starts unloading.
			$(window).on('beforeunload', function () {
				grantToken = null;
				clearTimers();
			});
		}
	};
})(window, jQuery);
