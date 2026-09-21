# Signal bridge for Resgrid

Resgrid supports private Signal commands, replies and proactive personnel notifications using the community-maintained [signal-cli-rest-api](https://github.com/bbernhard/signal-cli-rest-api) and [signal-cli](https://github.com/AsamK/signal-cli). This is an optional, self-hosted integration, not an official Signal bot service. Facebook Messenger remains excluded.

The operator supplies a dedicated Signal account and runs this bridge. Departments allow `Signal` and enable proactive notifications; each person links their own Signal account in **Profile → Messaging accounts**. Provider credentials and the sending account are system-wide, not department-owned. No database migration or new .NET package is needed.

## 1. Prepare the bridge

Use a dedicated account in the Signal mobile app. The REST bridge links as a secondary device; do not register over an existing personal account. It holds Signal account keys and processes decrypted message text, so its host and persistent volume belong inside the Resgrid trust boundary.

Copy `.env.example` to `.env` in this directory and fill in:

- `SIGNAL_BRIDGE_IMAGE`: an explicitly pinned, maintained `bbernhard/signal-cli-rest-api` image supporting `RECEIVE_WEBHOOK_URL` in `json-rpc` mode. Revalidate after bridge upgrades; Signal protocol compatibility requires keeping signal-cli current.
- `SIGNAL_GATEWAY_IMAGE`: a pinned official `nginx` Alpine image, with its standard template entrypoint and CA bundle.
- `SIGNAL_API_TOKEN` and `SIGNAL_WEBHOOK_SECRET`: two different random secrets, each 32–128 base64url characters (`A-Z`, `a-z`, `0-9`, `_`, `-`), without padding. For example, generate each with `python -c "import secrets; print(secrets.token_urlsafe(32))"`. Do not reuse the example names as values or commit secrets.
- `RESGRID_API_HOST`: your Resgrid API's public DNS hostname, without scheme, path or port. It must have a valid, trusted HTTPS certificate.

From this directory, start the temporary setup configuration:

```sh
docker compose -f compose.yml -f compose.setup.yml up -d
```

On the bridge host, open `http://127.0.0.1:8089/v1/qrcodelink?device_name=Resgrid`, then scan the QR code from **Signal → Settings → Linked devices** on the dedicated account's phone. For a remote host, use a secure local tunnel to port 8089. Once linked, recreate the containers using only the base configuration:

```sh
docker compose -f compose.yml up -d --force-recreate
```

This removes the temporary administration port while preserving `signal-data`. The gateway publishes only `127.0.0.1:8088` for authenticated outbound sends. It forwards `/v2/send` with the configured bearer token; other paths are denied. The bridge's administrative API has no published port in normal operation.

The bridge sends full JSON-RPC receive events to the gateway on port 8081. The gateway attaches the shared webhook secret and forwards them over verified HTTPS to `/api/v4/ChatbotPlatforms/Signal`. **Never publish port 8081, route public traffic to it, or attach untrusted containers to this Docker network.** The bridge does not add an authentication header itself; pointing its webhook directly at Resgrid will receive HTTP 401. Access logs are disabled on the gateway, and bridge logging is set to `warn` to avoid debug message bodies. Configure the host's log retention and volume access appropriately.

## 2. Configure Resgrid

Set these static configuration fields through the existing deployment configuration/secret mechanism, consistently on the API, MVC and workers:

| JSON key | Value |
|---|---|
| `ChatbotConfig.SignalBridgeUrl` | `http://127.0.0.1:8088` if Resgrid runs directly on that host; otherwise a secured HTTPS gateway origin |
| `ChatbotConfig.SignalAccountNumber` | Dedicated account's exact E.164 phone number, such as `+12025550123` |
| `ChatbotConfig.SignalBridgeApiToken` | Same value as gateway `SIGNAL_API_TOKEN` |
| `ChatbotConfig.SignalWebhookSecret` | Same value as gateway `SIGNAL_WEBHOOK_SECRET` |

Environment variable names follow the existing convention, for example `RESGRID:ChatbotConfig:SignalBridgeUrl`. Restart the hosts after changing configuration. The bridge URL must be an origin with no credentials, query, fragment or path; HTTPS is required except for loopback HTTP.

If Resgrid runs in containers or on other machines, their loopback address is **not** the bridge host. Put your existing HTTPS reverse proxy in front of the gateway's localhost port 8088, preserve the Authorization header and allow only the Resgrid hosts. Set `SignalBridgeUrl` to that HTTPS origin. Keep the internal webhook and raw administration API private. Private HTTP across hosts/containers is deliberately not accepted by the adapter.

Shared Redis, the chatbot queue consumer, matching encryption/environment settings, and existing account-linking prerequisites still apply; see the [messaging deployment guide](../../docs/architecture/messaging-platforms.md#deployment-prerequisites).

## 3. Enable departments and link people

1. In **Department settings → Chatbot settings**, enable the assistant and add `Signal` to allowed platforms (or use `*`). Enable proactive notifications for dispatches, messages and notifications.
2. Each person opens their own **Profile → Messaging accounts**, generates a code and sends `LINK ABC123` privately to the dedicated Signal account. The bot replies through Signal. People with hidden phone numbers are supported: Resgrid links the verified sender UUID, not their phone number or username.
3. Send `help`, then an authorized read-only command. `STOP` / `UNLINK` or the profile's unlink action removes the account link. Group messages, sync events, edits, receipts and attachment-only messages are ignored.

Signal gets the same private personnel dispatch/cancellation, messages, general/calendar notifications and trouble alerts as the existing native channel path, without an active chatbot session. Existing department policies, recipient checks and protected-data rules apply. There is no group-channel broadcasting, unit-device mirroring, weather delivery or internal Resgrid chat mirroring in this integration.

## 4. Acceptance checks and operating limits

- Validate the gateway configuration and access boundaries: unauthenticated `/v2/send` returns 401; exposed `/receive` returns 404; the raw bridge API and internal webhook port are inaccessible externally.
- After linking, verify a private command and its reply, then a test dispatch, message and notification to an authorized test person. Verify that removing `Signal` from the department allow-list or unlinking that person prevents subsequent broadcasts. No provider messages are sent by the automated tests.
- Verify the configured account matches webhook `params.account` (or `params.result.account` for a subscription wrapper). The adapter requires a UUID in `sourceUuid` and matching original envelope/data-message timestamps. Messages older than 15 minutes are ignored; malformed, future, unsigned or wrong-account messages cannot execute commands. Receipt IDs combine account and original timestamp, with sender identity included in the existing replay cache key.
- HTTP success plus the bridge's positive send timestamp means provider acceptance, not proof of delivery/read. Failed HTTP requests, missing timestamps and provider errors are not reported as successful sends. Blocking the account, untrusted identity keys, registration changes and provider throttling can interrupt delivery.
- The upstream webhook implementation attempts forwarding once; it has no durable retry spool. A bridge/API outage can therefore lose an inbound command before Resgrid's queue accepts it. Monitor bridge/gateway failures, and resend only after checking whether a command took effect. Once accepted, existing Resgrid execution claims and reply retries apply. Proactive delivery remains best effort with no new durable per-recipient outbox or read-receipt tracking; use the existing additional delivery channels as needed.
- To disable, clear the Signal configuration or remove it from department allow-lists. Preserve the bridge volume if you intend to resume the same account. Account registration, keys, credentials and live platform acceptance are operator rollout steps, not performed by the code change.

Protocol references: [bridge webhook implementation](https://github.com/bbernhard/signal-cli-rest-api/blob/master/src/client/jsonrpc2.go), [Signal JSON-RPC message format](https://github.com/AsamK/signal-cli/blob/master/man/signal-cli-jsonrpc.5.adoc), [REST send/UUID handling](https://github.com/bbernhard/signal-cli-rest-api/blob/master/src/client/client.go), and [NGINX proxy TLS verification](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_ssl_verify).

## Code verification (2026-09-20)

The Release solution build and all 488 focused chatbot/communication tests passed with zero failures or skips. Coverage includes Signal UUID linking, webhook authentication and event filtering, original timestamps, private replies, department-gated broadcasts, provider rejection, Unicode splitting, legacy/current response shapes and account-page availability. Both normal/setup Compose configurations validate and expose only their intended loopback ports. The local Docker engine was unavailable, so gateway runtime tests and live Signal acceptance have not been performed. No Signal account or production configuration was changed.
