/*
 * Learner portal lesson heartbeat (Training T5): posts seat time every 30 seconds while the lesson page is visible, and the watched share of
 * a hosted video. The server caps both by its own clock. Vanilla ES5 (no inline script; survives a strict script-src).
 */
(function () {
	'use strict';
	var holder = document.querySelector('[data-rgl-heartbeat]');
	if (!holder || !window.fetch || !window.FormData) {
		return;
	}
	var url = holder.getAttribute('data-url');
	var lesson = holder.getAttribute('data-lesson');
	var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
	var video = document.querySelector('video[data-rgl-video]');
	var last = Date.now();

	function send() {
		var now = Date.now();
		var seconds = Math.min(60, Math.round((now - last) / 1000));
		last = now;
		if (document.hidden || seconds <= 0) {
			return;
		}
		var data = new FormData();
		data.append('lesson', lesson);
		data.append('seconds', String(seconds));
		if (tokenInput) {
			data.append('__RequestVerificationToken', tokenInput.value);
		}
		if (video && video.duration > 0) {
			data.append('position', String(Math.floor(video.currentTime)));
			data.append('watched', String(Math.min(100, Math.floor(video.currentTime * 100 / video.duration))));
		}
		window.fetch(url, { method: 'POST', body: data, credentials: 'same-origin' })['catch'](function () { });
	}

	window.setInterval(send, 30000);
	document.addEventListener('visibilitychange', function () {
		if (document.hidden) {
			send();
		} else {
			last = Date.now();
		}
	});
})();
