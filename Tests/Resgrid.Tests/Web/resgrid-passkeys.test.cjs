// Passkey ceremonies for Core Web (passkey workbook section 12, slice 19): resgridPasskeys turns the server's JSON options
// into what navigator.credentials needs and returns the credential as base64url JSON, the shape the server verifies.
// Runs the real module in headless Chrome against its built-in virtual authenticator (DevTools WebAuthn domain) on an
// HTTPS origin served by request interception, so the browser's own WebAuthn implementation does the ceremonies.
//   node Tests/Resgrid.Tests/Web/resgrid-passkeys.test.cjs   (or through BrowserScriptTests / npm test)
// See browser-launch.cjs for RESGRID_PLAYWRIGHT_PATH and RESGRID_PLAYWRIGHT_CHANNEL.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { chromium } = require('./browser-launch.cjs').playwright();
const root = path.resolve(__dirname, '../../..');
const moduleSource = fs.readFileSync(path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/common/passkeys/resgrid.passkeys.js'), 'utf8');
const ORIGIN = 'https://web.resgrid.test';
const B64URL = /^[A-Za-z0-9_-]+$/;

const b64url = (buffer) => Buffer.from(buffer).toString('base64url');
const decodeJson = (value) => JSON.parse(Buffer.from(value, 'base64url').toString('utf8'));

// The options as the server's Fido2 library writes them: base64url strings for every binary field.
function creationOptions(challenge, exclude = []) {
    return JSON.stringify({
        rp: { id: 'web.resgrid.test', name: 'Resgrid' },
        user: { id: b64url(Buffer.from('user-handle-1')), name: 'user1', displayName: 'User One' },
        challenge: b64url(challenge),
        pubKeyCredParams: [{ type: 'public-key', alg: -7 }, { type: 'public-key', alg: -257 }],
        timeout: 60000,
        attestation: 'none',
        authenticatorSelection: { residentKey: 'required', requireResidentKey: true, userVerification: 'required' },
        excludeCredentials: exclude.map((id) => ({ type: 'public-key', id: id })),
        extensions: { credProps: true }
    });
}

function requestOptions(challenge, allow) {
    return JSON.stringify({
        challenge: b64url(challenge),
        timeout: 60000,
        rpId: 'web.resgrid.test',
        allowCredentials: allow.map((id) => ({ type: 'public-key', id: id })),
        userVerification: 'required'
    });
}

(async () => {
    const browser = await chromium.launch(require('./browser-launch.cjs').launchOptions());
    try {
        const context = await browser.newContext();
        await context.route(ORIGIN + '/**', (route) => route.fulfill({
            status: 200,
            contentType: 'text/html',
            body: '<!doctype html><html><head><meta charset="utf-8"></head><body><script>' + moduleSource + '</script></body></html>'
        }));
        const page = await context.newPage();
        await page.goto(ORIGIN + '/User/TwoFactor');
        assert.equal(await page.evaluate(() => window.isSecureContext), true, 'the test origin is a secure context');

        const cdp = await context.newCDPSession(page);
        await cdp.send('WebAuthn.enable');
        const { authenticatorId } = await cdp.send('WebAuthn.addVirtualAuthenticator', {
            options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true }
        });

        assert.equal(await page.evaluate(() => resgridPasskeys.isSupported()), true);

        // ---- Registration: the server's options in, base64url JSON out ----
        const createChallenge = crypto.randomBytes(32);
        const registered = await page.evaluate((options) => resgridPasskeys.register(options), creationOptions(createChallenge));
        assert.equal(registered.type, 'public-key');
        assert.match(registered.id, B64URL);
        assert.equal(registered.rawId, registered.id, 'rawId is the same bytes as id, base64url');
        assert.match(registered.response.attestationObject, B64URL);
        const createData = decodeJson(registered.response.clientDataJSON);
        assert.equal(createData.type, 'webauthn.create');
        assert.equal(createData.challenge, b64url(createChallenge), 'the challenge reached the authenticator unchanged');
        assert.equal(createData.origin, ORIGIN);
        assert.ok(!('toJSON' in registered), 'plain JSON, ready to post');

        // ---- Assertion with the passkey just made ----
        const getChallenge = crypto.randomBytes(32);
        const asserted = await page.evaluate((options) => resgridPasskeys.authenticate(options), requestOptions(getChallenge, [registered.id]));
        assert.equal(asserted.id, registered.id);
        assert.match(asserted.response.authenticatorData, B64URL);
        assert.match(asserted.response.signature, B64URL);
        assert.equal(asserted.response.userHandle, b64url(Buffer.from('user-handle-1')), 'the user handle comes back as the server sent it');
        const getData = decodeJson(asserted.response.clientDataJSON);
        assert.equal(getData.type, 'webauthn.get');
        assert.equal(getData.challenge, b64url(getChallenge));

        // ---- A second passkey on the same authenticator is refused by the browser, and says so ----
        const duplicate = await page.evaluate((options) => resgridPasskeys.register(options).then(() => 'registered', (error) => error.outcome),
            creationOptions(crypto.randomBytes(32), [registered.id]));
        assert.equal(duplicate, 'already_registered');

        // ---- A refused prompt is a cancellation, never a failed verification ----
        await cdp.send('WebAuthn.setUserVerified', { authenticatorId, isUserVerified: false });
        const cancelled = await page.evaluate((options) => resgridPasskeys.authenticate(options).then(() => 'verified', (error) => error.outcome),
            requestOptions(crypto.randomBytes(32), [registered.id]));
        assert.equal(cancelled, 'cancelled');

        // ---- The hand-built JSON (browsers without toJSON) matches the same shape ----
        const manual = await page.evaluate(() => {
            const bytes = (values) => new Uint8Array(values).buffer;
            const assertion = resgridPasskeys.toJson({
                id: 'abc', rawId: bytes([251, 255, 254]), type: 'public-key', authenticatorAttachment: 'platform',
                getClientExtensionResults: () => ({}),
                response: { clientDataJSON: bytes([123, 125]), authenticatorData: bytes([1, 2]), signature: bytes([3]), userHandle: null }
            });
            const attestation = resgridPasskeys.toJson({
                id: 'def', rawId: bytes([1]), type: 'public-key', getClientExtensionResults: () => ({ credProps: { rk: true } }),
                response: { clientDataJSON: bytes([123, 125]), attestationObject: bytes([9, 9]), getTransports: () => ['internal'] }
            });
            return { assertion, attestation };
        });
        assert.deepEqual(manual.assertion, {
            id: 'abc', rawId: '-__-', type: 'public-key', clientExtensionResults: {}, authenticatorAttachment: 'platform',
            response: { clientDataJSON: 'e30', authenticatorData: 'AQI', signature: 'Aw', userHandle: null }
        }, 'base64url without padding, with - and _');
        assert.deepEqual(manual.attestation.response, { clientDataJSON: 'e30', attestationObject: 'CQk', transports: ['internal'] });
        assert.deepEqual(manual.attestation.clientExtensionResults, { credProps: { rk: true } });

        // ---- The option conversion leaves everything but the binary fields alone ----
        const converted = await page.evaluate((options) => {
            const o = resgridPasskeys.creationOptions(options);
            return { challenge: o.challenge instanceof ArrayBuffer, userId: new TextDecoder().decode(o.user.id), rp: o.rp.id, uv: o.authenticatorSelection.userVerification };
        }, creationOptions(crypto.randomBytes(8)));
        assert.deepEqual(converted, { challenge: true, userId: 'user-handle-1', rp: 'web.resgrid.test', uv: 'required' });

        // ---- A browser without WebAuthn is told so before any request is made ----
        const unsupported = await page.evaluate((options) => {
            delete window.PublicKeyCredential;
            window.PublicKeyCredential = undefined;
            return resgridPasskeys.register(options).then(() => 'registered', (error) => error.outcome);
        }, creationOptions(crypto.randomBytes(32)));
        assert.equal(unsupported, 'not_supported');

        console.log('resgrid-passkeys: all checks passed');
    } finally {
        await browser.close();
    }
})().catch((error) => {
    console.error(error);
    process.exit(1);
});
