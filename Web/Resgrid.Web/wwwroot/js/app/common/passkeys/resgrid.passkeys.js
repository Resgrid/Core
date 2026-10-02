// Passkey ceremonies for Resgrid Web (passkey plan sections 6.1, 7.1 and 8.1). The server sends WebAuthn options as JSON
// with base64url binary fields; this turns them into what navigator.credentials needs, runs the browser's prompt, and
// returns the credential as PublicKeyCredential.toJSON() would (base64url strings), which is what the server verifies.
// Nothing here stores a credential, a challenge or a response: each lives only for the one ceremony.
(function (window) {
	'use strict';

	function toBuffer(value) {
		if (value instanceof ArrayBuffer)
			return value;
		if (ArrayBuffer.isView(value))
			return value.buffer.slice(value.byteOffset, value.byteOffset + value.byteLength);
		var base64 = String(value).replace(/-/g, '+').replace(/_/g, '/');
		while (base64.length % 4)
			base64 += '=';
		var binary = window.atob(base64);
		var bytes = new Uint8Array(binary.length);
		for (var i = 0; i < binary.length; i++)
			bytes[i] = binary.charCodeAt(i);
		return bytes.buffer;
	}

	function toBase64Url(buffer) {
		if (buffer === null || buffer === undefined)
			return null;
		var bytes = new Uint8Array(buffer instanceof ArrayBuffer ? buffer : buffer.buffer.slice(buffer.byteOffset, buffer.byteOffset + buffer.byteLength));
		var binary = '';
		for (var i = 0; i < bytes.length; i++)
			binary += String.fromCharCode(bytes[i]);
		return window.btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
	}

	function parse(options) {
		return typeof options === 'string' ? JSON.parse(options) : JSON.parse(JSON.stringify(options || {}));
	}

	function descriptors(list) {
		return (list || []).map(function (item) {
			var descriptor = { type: item.type || 'public-key', id: toBuffer(item.id) };
			if (item.transports)
				descriptor.transports = item.transports;
			return descriptor;
		});
	}

	function creationOptions(options) {
		var o = parse(options);
		o.challenge = toBuffer(o.challenge);
		if (o.user)
			o.user.id = toBuffer(o.user.id);
		if (o.excludeCredentials)
			o.excludeCredentials = descriptors(o.excludeCredentials);
		return o;
	}

	function requestOptions(options) {
		var o = parse(options);
		o.challenge = toBuffer(o.challenge);
		if (o.allowCredentials)
			o.allowCredentials = descriptors(o.allowCredentials);
		return o;
	}

	// The credential as the server expects it: WebAuthn Level 3 toJSON() where the browser has it, the same shape
	// built by hand where it does not.
	function toJson(credential) {
		if (typeof credential.toJSON === 'function') {
			try {
				return credential.toJSON();
			} catch (e) {
				// Some implementations throw for extension results they cannot serialise; fall through to the manual shape.
			}
		}

		var response = credential.response;
		var json = {
			id: credential.id,
			rawId: toBase64Url(credential.rawId),
			type: credential.type,
			clientExtensionResults: typeof credential.getClientExtensionResults === 'function' ? credential.getClientExtensionResults() : {},
			response: { clientDataJSON: toBase64Url(response.clientDataJSON) }
		};
		if (credential.authenticatorAttachment)
			json.authenticatorAttachment = credential.authenticatorAttachment;

		if (response.attestationObject) {
			json.response.attestationObject = toBase64Url(response.attestationObject);
			if (typeof response.getTransports === 'function')
				json.response.transports = response.getTransports();
		} else {
			json.response.authenticatorData = toBase64Url(response.authenticatorData);
			json.response.signature = toBase64Url(response.signature);
			json.response.userHandle = response.userHandle ? toBase64Url(response.userHandle) : null;
		}

		return json;
	}

	function isSupported() {
		return !!(window.PublicKeyCredential && window.navigator.credentials && window.navigator.credentials.create && window.navigator.credentials.get);
	}

	// A closed or refused prompt is the user's choice, never a failed verification (plan section 11): callers show it as
	// "cancelled" and count nothing against the user.
	function outcome(error) {
		var name = error && error.name;
		if (name === 'NotAllowedError' || name === 'AbortError')
			return 'cancelled';
		if (name === 'InvalidStateError')
			return 'already_registered';
		if (name === 'NotSupportedError' || name === 'SecurityError')
			return 'not_supported';
		return 'failed';
	}

	function run(kind, options) {
		if (!isSupported())
			return Promise.reject({ outcome: 'not_supported' });

		var request = kind === 'create'
			? { publicKey: creationOptions(options) }
			: { publicKey: requestOptions(options) };

		return window.navigator.credentials[kind](request).then(function (credential) {
			if (!credential)
				throw { outcome: 'cancelled' };
			return toJson(credential);
		}, function (error) {
			throw { outcome: outcome(error), name: error && error.name };
		});
	}

	window.resgridPasskeys = {
		isSupported: isSupported,
		register: function (options) { return run('create', options); },
		authenticate: function (options) { return run('get', options); },
		// Exposed for tests.
		creationOptions: creationOptions,
		requestOptions: requestOptions,
		toJson: toJson,
		toBase64Url: toBase64Url
	};
})(window);
