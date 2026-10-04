/*
 * Resgrid web push service worker.
 *
 * The page mints its FCM web token against this registration (firebase/messaging getToken with
 * serviceWorkerRegistration), but Firebase is not loaded here: every push is shown below, so the
 * notification and its click routing stay ours. Core sends the fields in the FCM webpush block
 * (NovuProvider.SendNotification): data carries title, message, eventCode, type and category.
 */
'use strict';

self.addEventListener('install', function () {
	self.skipWaiting();
});

self.addEventListener('activate', function (event) {
	event.waitUntil(self.clients.claim());
});

function readPush(event) {
	var payload = {};
	if (event.data) {
		try {
			payload = event.data.json() || {};
		} catch (e) {
			payload = { notification: { body: event.data.text() } };
		}
	}

	var notification = payload.notification || {};
	var data = payload.data || {};

	return {
		title: data.title || notification.title || payload.title || 'Resgrid',
		body: data.message || notification.body || data.body || payload.body || payload.message || '',
		eventCode: data.eventCode || payload.eventCode || '',
		type: data.type || '',
		category: data.category || ''
	};
}

// Event codes as Core sends them: C123 / C:123 (call), M123 (message), N123 (notification), t:/g: (chat),
// W (weather), NWO (work order), NA (sign-in approval) and CT (communication test).
function parseEventCode(eventCode) {
	var code = String(eventCode || '').trim();
	var multi = /^(CT|NWO|NA):?(.*)$/i.exec(code);
	if (multi) {
		return { kind: multi[1].toUpperCase(), id: multi[2] };
	}

	var single = /^([A-Za-z]):?(.*)$/.exec(code);
	return single ? { kind: single[1].toUpperCase(), id: single[2] } : { kind: '', id: '' };
}

function urlFor(eventCode) {
	var parsed = parseEventCode(eventCode);
	var numericId = /^\d+$/.test(parsed.id) ? parsed.id : null;

	switch (parsed.kind) {
		case 'C':
			return numericId ? '/User/Dispatch/ViewCall?callId=' + numericId : '/User/Dispatch/Dashboard';
		case 'M':
			return numericId ? '/User/Messages/ViewMessage?messageId=' + numericId : '/User/Messages/Inbox';
		case 'T':
		case 'G':
			return '/User/Chat';
		case 'W':
			return '/User/WeatherAlerts';
		default:
			return '/User/Home/Dashboard';
	}
}

self.addEventListener('push', function (event) {
	var push = readPush(event);
	var isCall = push.category === 'calls' || parseEventCode(push.eventCode).kind === 'C';

	event.waitUntil(
		self.registration.showNotification(push.title, {
			body: push.body,
			icon: '/images/android-chrome-192x192.png',
			tag: push.eventCode || undefined,
			renotify: !!push.eventCode,
			requireInteraction: isCall,
			data: { eventCode: push.eventCode, type: push.type, url: urlFor(push.eventCode) }
		})
	);
});

self.addEventListener('notificationclick', function (event) {
	event.notification.close();

	var target = new URL('/User/Home/Dashboard', self.location.origin).href;
	try {
		var requested = new URL((event.notification.data && event.notification.data.url) || target, self.location.origin);
		if (requested.origin === self.location.origin) { target = requested.href; }
	} catch (e) {
		// An old or malformed notification still opens the dashboard.
	}

	// A tab already showing the target is brought forward; otherwise the target opens in a new tab, so an
	// open tab mid-form is never navigated away from.
	event.waitUntil(
		self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(function (windows) {
			for (var i = 0; i < windows.length; i++) {
				if (windows[i].url === target && 'focus' in windows[i]) {
					return windows[i].focus();
				}
			}

			return self.clients.openWindow(target);
		})
	);
});
