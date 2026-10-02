// Passkeys on the Web account security page (passkey plan sections 6.1 and 6.5). The server decides everything: this
// page asks it for ceremony options, runs the browser's prompt through resgridPasskeys, and sends the result back. When
// the server answers that the user must first confirm their password or verify again, it names the page to go to and
// this script follows it; nothing is decided or stored here. Text is inserted with text(), never as markup.
(function (window, $) {
	'use strict';

	var settings = null; // { optionsUrl, completeUrl, renameUrl, removeUrl, pageUrl, signInUrl, messages, navigate }

	function go(url) {
		if (settings && typeof settings.navigate === 'function')
			settings.navigate(url);
		else
			window.location.assign(url);
	}

	function token() {
		return $('#passkeyAntiForgery input[name="__RequestVerificationToken"]').val();
	}

	function message(key) {
		var messages = (settings && settings.messages) || {};
		return messages[key] || messages.failed || '';
	}

	function showError(key) {
		$('#passkeyStatus').hide();
		$('#passkeyError').text(message(key)).show();
	}

	function busy(on) {
		$('#passkeyAdd, .passkey-rename, .passkey-remove').prop('disabled', on);
	}

	// A refusal the user can act on: confirm the password, verify again, or sign in again. Anything else is shown.
	function refused(response) {
		busy(false);
		if (response && response.redirect) {
			go(response.redirect);
			return;
		}

		var code = response && response.error;
		showError(code === 'passkey_limit_reached' ? 'limit' : code === 'passkeys_unavailable' ? 'unavailable' : 'failed');
	}

	function reload(status) {
		go(settings.pageUrl + (settings.pageUrl.indexOf('?') >= 0 ? '&' : '?') + 'passkeyStatus=' + status + '#passkeys');
	}

	function ceremonyFailed(error) {
		busy(false);
		var outcome = error && error.outcome;
		showError(outcome === 'cancelled' ? 'cancelled' : outcome === 'not_supported' ? 'notSupported' : outcome === 'already_registered' ? 'alreadyRegistered' : 'failed');
	}

	function add() {
		if (!window.resgridPasskeys || !window.resgridPasskeys.isSupported()) {
			showError('notSupported');
			return;
		}

		busy(true);
		var name = ($('#passkeyName').val() || '').trim();
		$.post(settings.optionsUrl, { __RequestVerificationToken: token() }).done(function (start) {
			if (!start || !start.success) {
				refused(start);
				return;
			}

			window.resgridPasskeys.register(start.options).then(function (credential) {
				$.post(settings.completeUrl, {
					__RequestVerificationToken: token(),
					requestId: start.requestId,
					credential: JSON.stringify(credential),
					displayName: name
				}).done(function (done) {
					if (done && done.success)
						reload('added');
					else
						refused(done);
				}).fail(function () {
					refused(null);
				});
			}, ceremonyFailed);
		}).fail(function () {
			refused(null);
		});
	}

	function rename(row) {
		var current = row.find('.passkey-name').text();
		var name = window.prompt(message('renamePrompt'), current);
		if (name === null || !name.trim() || name.trim() === current)
			return;

		busy(true);
		$.post(settings.renameUrl, { __RequestVerificationToken: token(), id: row.data('passkey-id'), displayName: name.trim() })
			.done(function (done) {
				if (done && done.success)
					reload('renamed');
				else
					refused(done);
			}).fail(function () {
				refused(null);
			});
	}

	function remove(row) {
		if (!window.confirm(message('removeConfirm')))
			return;

		busy(true);
		$.post(settings.removeUrl, { __RequestVerificationToken: token(), id: row.data('passkey-id') })
			.done(function (done) {
				if (!done || !done.success) {
					refused(done);
					return;
				}

				// A passkey this session signed in with ends this session too.
				if (done.signedOut)
					go(settings.signInUrl);
				else
					reload('removed');
			}).fail(function () {
				refused(null);
			});
	}

	window.resgridAccountPasskeys = {
		init: function (options) {
			settings = options || {};
			$('#passkeyAdd').on('click', add);
			$(document).on('click', '.passkey-rename', function () { rename($(this).closest('tr')); });
			$(document).on('click', '.passkey-remove', function () { remove($(this).closest('tr')); });
		}
	};
})(window, jQuery);
