/*
 * The Resgrid signing page (e-Signature S2): listens to the embedded e-sign signer (postMessage from its origin only) and,
 * when it reports the document completed or rejected, asks the server to re-fetch the envelope (the server trusts only the
 * re-fetched state, never this message).
 */
(function () {
	'use strict';

	var frame = document.getElementById('rg-sign-embed');
	var done = document.getElementById('rg-sign-done');
	if (!frame || !done) {
		return;
	}

	var origin = frame.getAttribute('data-origin');
	var submitted = false;

	window.addEventListener('message', function (event) {
		if (!origin || event.origin !== origin || submitted) {
			return;
		}

		var data = event.data;
		if (typeof data === 'string') {
			try {
				data = JSON.parse(data);
			} catch (e) {
				return;
			}
		}

		var action = data && (data.action || data.type);
		if (action === 'document-completed' || action === 'document-rejected') {
			submitted = true;
			done.submit();
		}
	});
})();
