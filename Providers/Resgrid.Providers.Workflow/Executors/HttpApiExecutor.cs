using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Workflow.Executors
{
	public class HttpApiExecutor : IWorkflowActionExecutor
	{
		/// <summary>OAuth2 tokens by a hash of (token URL, client id, secret, scope, audience); reused until 60 seconds before expiry.</summary>
		private static readonly ConcurrentDictionary<string, CachedToken> TokenCache = new ConcurrentDictionary<string, CachedToken>(StringComparer.Ordinal);
		private static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromSeconds(60);

		/// <summary>Headers a protected step's action config may never set: they would redirect or re-authenticate the request.</summary>
		private static readonly HashSet<string> ProtectedForbiddenHeaders = ProtectedStepOptions.ForbiddenHeaders;

		private readonly Func<bool, HttpMessageHandler> _handlerFactory;
		private readonly Func<string, Task<(bool IsAllowed, string Reason)>> _urlValidator;

		public HttpApiExecutor() : this(null, null)
		{
		}

		/// <summary>Test seam: a handler factory (the flag is true for protected mode) and a URL validator in place of the SSRF guard.</summary>
		internal HttpApiExecutor(Func<bool, HttpMessageHandler> handlerFactory, Func<string, Task<(bool IsAllowed, string Reason)>> urlValidator)
		{
			_handlerFactory = handlerFactory ?? CreateHandler;
			_urlValidator = urlValidator ?? (url => SsrfGuard.ValidateUrlAsync(url, requireHttps: true));
		}

		public WorkflowActionType ActionType => WorkflowActionType.CallApiPost;

		public async Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken)
		{
			if (context.ProtectedMode)
				return await ExecuteProtectedAsync(context, cancellationToken);

			try
			{
				var config = string.IsNullOrWhiteSpace(context.ActionConfigJson)
					? new HttpActionConfig()
					: JsonConvert.DeserializeObject<HttpActionConfig>(context.ActionConfigJson) ?? new HttpActionConfig();

				if (string.IsNullOrWhiteSpace(config.Url))
					return WorkflowActionResult.Failed("HTTP request failed.", "No URL is configured for this workflow step. Please set the 'Url' field in the action config.");

				if (!Uri.TryCreate(config.Url, UriKind.Absolute, out _))
					return WorkflowActionResult.Failed("HTTP request failed.", $"The configured URL '{config.Url}' is not a valid absolute URI.");

				// ── SSRF protection ──────────────────────────────────────────────────
				var (ssrfAllowed, ssrfReason) = await _urlValidator(config.Url);
				if (!ssrfAllowed)
					return WorkflowActionResult.Failed("HTTP request blocked.", ssrfReason);
				// ── End SSRF protection ──────────────────────────────────────────────

				var cred = ReadCredential(context);
				var authType = ResolveAuthType(cred, context.CredentialType);

				if (cred != null)
				{
					switch (authType)
					{
						case "bearer" when string.IsNullOrWhiteSpace(cred.Token):
							return WorkflowActionResult.Failed("HTTP request failed.", "Auth type is 'bearer' but no token is set in the credential. Please update the credential with a valid token.");
						case "basic" when string.IsNullOrWhiteSpace(cred.Username):
							return WorkflowActionResult.Failed("HTTP request failed.", "Auth type is 'basic' but no username is set in the credential.");
						case "apikey" when string.IsNullOrWhiteSpace(cred.ApiKey):
							return WorkflowActionResult.Failed("HTTP request failed.", "Auth type is 'apikey' but no API key is set in the credential.");
						case "oauth2" when string.IsNullOrWhiteSpace(cred.TokenUrl) || string.IsNullOrWhiteSpace(cred.ClientId) ||
							(cred.IsPrivateKeyJwt ? WorkflowJwtKeys.Current(cred.SigningKeys) == null : string.IsNullOrWhiteSpace(cred.ClientSecret)):
							return WorkflowActionResult.Failed("HTTP request failed.", "Auth type is 'oauth2' but the credential is missing its token URL, client id, or client secret / signing key.");
					}
				}

				// OAuth2 uses the no-redirect handler: a 307/308 from a token endpoint would otherwise re-send the client secret
				// to a host the SSRF guard never checked.
				using var client = new HttpClient(_handlerFactory(authType == "oauth2")) { Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 30) };

				if (authType == "oauth2")
				{
					if (!Uri.TryCreate(cred.TokenUrl, UriKind.Absolute, out var tokenUri) || tokenUri.Scheme != Uri.UriSchemeHttps)
						return WorkflowActionResult.Failed("HTTP request failed.", "The OAuth2 token URL must be an absolute https URL.");
					var (tokenAllowed, tokenReason) = await _urlValidator(cred.TokenUrl);
					if (!tokenAllowed)
						return WorkflowActionResult.Failed("HTTP request blocked.", tokenReason);

					var token = await GetOAuthTokenAsync(client, cred, cancellationToken);
					if (token.AccessToken == null)
						return WorkflowActionResult.Failed("OAuth2 token request failed.", token.Status.HasValue ? $"The token endpoint returned HTTP {token.Status}." : "The token endpoint could not be reached.");
					client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
				}
				else
				{
					ApplyAuth(client.DefaultRequestHeaders, cred, authType);
				}

				if (config.Headers != null)
					foreach (var h in config.Headers)
						client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);

				if (!string.IsNullOrWhiteSpace(config.IdempotencyHeader) && !string.IsNullOrWhiteSpace(context.IdempotencyKey) &&
					!ProtectedStepOptions.ForbiddenHeaders.Contains(config.IdempotencyHeader))
					client.DefaultRequestHeaders.TryAddWithoutValidation(config.IdempotencyHeader, context.IdempotencyKey);
				if (!string.IsNullOrWhiteSpace(config.IfNoneExist) && config.IfNoneExist.IndexOfAny(new[] { '\r', '\n' }) < 0)
					client.DefaultRequestHeaders.TryAddWithoutValidation("If-None-Exist", config.IfNoneExist);

				var method = (WorkflowActionType)context.ActionType switch
				{
					WorkflowActionType.CallApiGet    => HttpMethod.Get,
					WorkflowActionType.CallApiPut    => HttpMethod.Put,
					WorkflowActionType.CallApiDelete => HttpMethod.Delete,
					_                                => HttpMethod.Post
				};

				var request = new HttpRequestMessage(method, config.Url);

				if (method != HttpMethod.Get && method != HttpMethod.Delete)
				{
					var contentType = string.IsNullOrWhiteSpace(config.ContentType) ? "application/json" : config.ContentType;
					request.Content = new StringContent(context.RenderedContent ?? string.Empty, Encoding.UTF8, contentType);
				}

				var response = await client.SendAsync(request, cancellationToken);
				var body = await response.Content.ReadAsStringAsync(cancellationToken);
				var snippet = body?.Length > 4000 ? body.Substring(0, 4000) : body;

				if (response.IsSuccessStatusCode)
					return WorkflowActionResult.Succeeded($"HTTP {(int)response.StatusCode}: {snippet}");

				return WorkflowActionResult.Failed($"HTTP {(int)response.StatusCode}", snippet);
			}
			catch (TaskCanceledException)
			{
				return WorkflowActionResult.Failed("Request timed out.", "The HTTP request exceeded the configured timeout.");
			}
			catch (Exception ex)
			{
				return WorkflowActionResult.Failed("HTTP request failed.", ex.Message);
			}
		}

		/// <summary>
		/// A Protected Workflow send. Refuses anything but POST/PUT to the pinned https host (re-checked on the URL as
		/// rendered), never follows a redirect (a 3xx is blocked_host), requires TLS 1.2 or later, applies the protected
		/// timeout, and reports only the status code and its standard reason phrase. The response body is never read:
		/// endpoints can echo the payload back. Nothing here logs, and error details are fixed codes plus exception types.
		/// </summary>
		private async Task<WorkflowActionResult> ExecuteProtectedAsync(WorkflowActionContext context, CancellationToken cancellationToken)
		{
			var body = Encoding.UTF8.GetBytes(context.RenderedContent ?? string.Empty);
			var sha256 = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();

			WorkflowActionResult Refuse(string outcome, string code, int? status = null) => new WorkflowActionResult
			{
				Success = false,
				ResultMessage = status.HasValue ? StatusLine(status.Value) : "Protected request refused.",
				ErrorDetail = code,
				HttpStatus = status,
				ProtectedOutcome = outcome
			};

			var actionType = (WorkflowActionType)context.ActionType;
			if (actionType != WorkflowActionType.CallApiPost && actionType != WorkflowActionType.CallApiPut)
				return Refuse(ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.ActionNotAllowed);

			HttpActionConfig config;
			try
			{
				config = string.IsNullOrWhiteSpace(context.ActionConfigJson)
					? new HttpActionConfig()
					: JsonConvert.DeserializeObject<HttpActionConfig>(context.ActionConfigJson) ?? new HttpActionConfig();
			}
			catch (JsonException)
			{
				return Refuse(ProtectedWorkflowDisclosureOutcomes.BlockedHost, ProtectedWorkflowErrorCodes.HostMismatch);
			}

			// The host is checked again here, on the URL exactly as it will be requested.
			if (!ProtectedWorkflowValidator.TryGetRenderedHttpsHost(config.Url, out var host, out var urlError))
				return Refuse(ProtectedWorkflowDisclosureOutcomes.BlockedHost,
					urlError == ProtectedWorkflowValidator.SchemeNotHttps ? ProtectedWorkflowErrorCodes.SchemeNotHttps : ProtectedWorkflowErrorCodes.HostMismatch);
			if (string.IsNullOrWhiteSpace(context.PinnedHost) ||
				!string.Equals(host, ProtectedWorkflowFingerprint.NormalizeHost(context.PinnedHost), StringComparison.Ordinal))
				return Refuse(ProtectedWorkflowDisclosureOutcomes.BlockedHost, ProtectedWorkflowErrorCodes.HostMismatch);

			var (allowed, _) = await _urlValidator(config.Url);
			if (!allowed)
				return Refuse(ProtectedWorkflowDisclosureOutcomes.BlockedHost, ProtectedWorkflowErrorCodes.HostMismatch);

			var cred = ReadCredential(context);
			var authType = ResolveAuthType(cred, context.CredentialType);
			if (cred == null || authType == null ||
				(authType == "basic" && !DataProtectionConfig.ProtectedWorkflowAllowHttpBasicCredentials))
				return Refuse(ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.CredentialNotAllowed);

			// OAuth2 authenticates exactly the way the release pinned (client secret or a signed private_key_jwt assertion).
			if (authType == "oauth2" && !string.Equals(WorkflowJwtKeys.NormalizeAuthMethod(cred.AuthMethod), context.PinnedAuthMethod, StringComparison.Ordinal))
				return Refuse(ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.AuthMethodMismatch);

			// Content type, success rule, capture and idempotency (validated before approval; re-read on the rendered config).
			var options = ProtectedStepOptions.Read(context.ActionConfigJson, out var optionErrors, DataProtectionConfig.ProtectedWorkflowMaxCaptureKeys);
			if (optionErrors.Count > 0 || !ProtectedStepOptions.IsAllowedContentType(options.ContentType))
				return Refuse(ProtectedWorkflowDisclosureOutcomes.FailedValidation, $"{ProtectedWorkflowErrorCodes.PayloadInvalid}: rule={optionErrors.FirstOrDefault() ?? ProtectedStepOptions.ContentTypeNotAllowed}");

			try
			{
				using var client = new HttpClient(_handlerFactory(true))
				{
					Timeout = TimeSpan.FromSeconds(Math.Max(1, DataProtectionConfig.ProtectedWorkflowHttpTimeoutSeconds))
				};

				string bearer = null;
				if (authType == "oauth2")
				{
					// The token endpoint is pinned too: the client secret goes nowhere but the approved host.
					if (!ProtectedWorkflowValidator.TryGetRenderedHttpsHost(cred.TokenUrl, out var tokenHost, out _) ||
						string.IsNullOrWhiteSpace(context.PinnedTokenHost) ||
						!string.Equals(tokenHost, ProtectedWorkflowFingerprint.NormalizeHost(context.PinnedTokenHost), StringComparison.Ordinal))
						return Refuse(ProtectedWorkflowDisclosureOutcomes.BlockedHost, ProtectedWorkflowErrorCodes.TokenHostMismatch);

					var (tokenAllowed, _) = await _urlValidator(cred.TokenUrl);
					if (!tokenAllowed)
						return Refuse(ProtectedWorkflowDisclosureOutcomes.BlockedHost, ProtectedWorkflowErrorCodes.TokenHostMismatch);

					if (cred.IsPrivateKeyJwt && WorkflowJwtKeys.Current(cred.SigningKeys) == null)
						return Refuse(ProtectedWorkflowDisclosureOutcomes.BlockedRelease, ProtectedWorkflowErrorCodes.SigningKeyUnavailable);

					var token = await GetOAuthTokenAsync(client, cred, cancellationToken);
					if (token.Redirected)
						return Refuse(ProtectedWorkflowDisclosureOutcomes.BlockedHost, ProtectedWorkflowErrorCodes.Redirected, token.Status);
					if (token.AccessToken == null)
						return Refuse(ProtectedWorkflowDisclosureOutcomes.FailedHttp, ProtectedWorkflowErrorCodes.OAuthTokenFailed, token.Status);
					bearer = token.AccessToken;
				}

				using var request = new HttpRequestMessage(actionType == WorkflowActionType.CallApiPut ? HttpMethod.Put : HttpMethod.Post, config.Url);
				request.Content = new ByteArrayContent(body);
				request.Content.Headers.ContentType = ParseContentType(options.ContentType);

				if (config.Headers != null)
					foreach (var h in config.Headers)
						if (!ProtectedForbiddenHeaders.Contains(h.Key ?? string.Empty) &&
							!string.Equals(h.Key, options.IdempotencyHeader, StringComparison.OrdinalIgnoreCase))
							request.Headers.TryAddWithoutValidation(h.Key, h.Value);

				// The delivery's idempotency key and a FHIR conditional create: a retried delivery is recognized, not duplicated.
				if (options.IdempotencyHeader != null && !string.IsNullOrWhiteSpace(context.IdempotencyKey))
					request.Headers.TryAddWithoutValidation(options.IdempotencyHeader, context.IdempotencyKey);
				if (options.IfNoneExist != null)
					request.Headers.TryAddWithoutValidation("If-None-Exist", options.IfNoneExist);

				if (bearer != null)
					request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
				else
					ApplyAuth(request.Headers, cred, authType);

				// Headers first. The body is read only when a success rule or a capture needs it, capped, and never logged
				// (an endpoint can echo the payload back).
				using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
				var status = (int)response.StatusCode;

				if (status >= 300 && status < 400)
					return new WorkflowActionResult
					{
						Success = false,
						ResultMessage = StatusLine(status),
						ErrorDetail = ProtectedWorkflowErrorCodes.Redirected,
						HttpStatus = status,
						PayloadSha256 = sha256,
						PayloadBytes = body.Length,
						ProtectedOutcome = ProtectedWorkflowDisclosureOutcomes.BlockedHost
					};

				string responseBody = null;
				if (options.NeedsResponseBody)
				{
					var read = await ReadCappedAsync(response, Math.Max(1, DataProtectionConfig.ProtectedWorkflowMaxResponseBytes), cancellationToken);
					if (read.TooLarge)
						return new WorkflowActionResult
						{
							Success = false,
							ResultMessage = StatusLine(status),
							ErrorDetail = ProtectedWorkflowErrorCodes.ResponseTooLarge,
							HttpStatus = status,
							PayloadSha256 = sha256,
							PayloadBytes = body.Length,
							ProtectedOutcome = ProtectedWorkflowDisclosureOutcomes.FailedResponseTooLarge
						};
					responseBody = read.Body;
				}

				var evaluation = ProtectedResponseRules.Evaluate(options, status, responseBody, name => HeaderValue(response, name));
				responseBody = null;

				if (evaluation.Accepted)
					return new WorkflowActionResult
					{
						Success = true,
						ResultMessage = evaluation.Missing.Count > 0
							? $"{StatusLine(status)} capture_missing=[{string.Join(",", evaluation.Missing)}]"
							: StatusLine(status),
						HttpStatus = status,
						PayloadSha256 = sha256,
						PayloadBytes = body.Length,
						CapturedValues = evaluation.Captured.Count > 0 ? evaluation.Captured : null
					};

				return new WorkflowActionResult
				{
					Success = false,
					ResultMessage = StatusLine(status),
					ErrorDetail = evaluation.RuleRejected
						? $"{ProtectedWorkflowErrorCodes.AckRejected}: {evaluation.Detail}"
						: $"{ProtectedWorkflowErrorCodes.HttpFailed}: HTTP {status}",
					HttpStatus = status,
					PayloadSha256 = sha256,
					PayloadBytes = body.Length,
					ProtectedOutcome = evaluation.RuleRejected ? ProtectedWorkflowDisclosureOutcomes.FailedAck : ProtectedWorkflowDisclosureOutcomes.FailedHttp
				};
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				return new WorkflowActionResult
				{
					Success = false,
					ResultMessage = "Request timed out.",
					ErrorDetail = ProtectedWorkflowErrorCodes.HttpTimeout,
					PayloadSha256 = sha256,
					PayloadBytes = body.Length,
					ProtectedOutcome = ProtectedWorkflowDisclosureOutcomes.FailedHttp
				};
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is AuthenticationException || ex is System.IO.IOException)
			{
				// No exception message: nothing guarantees it cannot quote the request. The type plus the transport's own
				// value-free error enum is enough to tell DNS from TLS from a reset connection.
				return new WorkflowActionResult
				{
					Success = false,
					ResultMessage = "HTTP request failed.",
					ErrorDetail = TransportError(ex),
					PayloadSha256 = sha256,
					PayloadBytes = body.Length,
					ProtectedOutcome = ProtectedWorkflowDisclosureOutcomes.FailedHttp
				};
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				return new WorkflowActionResult
				{
					Success = false,
					ResultMessage = "HTTP request failed.",
					ErrorDetail = ProtectedWorkflowLogText.Error(ProtectedWorkflowErrorCodes.HttpFailed, ex),
					PayloadSha256 = sha256,
					PayloadBytes = body.Length,
					ProtectedOutcome = ProtectedWorkflowDisclosureOutcomes.FailedHttp
				};
			}
		}

		/// <summary>The response body as text, or TooLarge once it passes <paramref name="maxBytes"/> (the declared length is checked first).</summary>
		private static async Task<(string Body, bool TooLarge)> ReadCappedAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
		{
			if (response.Content == null)
				return (string.Empty, false);
			if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > maxBytes)
				return (null, true);

			await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
			var buffer = new byte[Math.Min(maxBytes + 1, 81920)];
			using var collected = new System.IO.MemoryStream();
			int read;
			while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
			{
				collected.Write(buffer, 0, read);
				if (collected.Length > maxBytes)
					return (null, true);
			}

			var charset = response.Content.Headers.ContentType?.CharSet;
			Encoding encoding;
			try { encoding = string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"')); }
			catch (ArgumentException) { encoding = Encoding.UTF8; }
			return (encoding.GetString(collected.GetBuffer(), 0, (int)collected.Length), false);
		}

		private static string HeaderValue(HttpResponseMessage response, string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				return null;
			if (response.Headers.TryGetValues(name, out var values))
				return values.FirstOrDefault();
			if (response.Content != null && response.Content.Headers.TryGetValues(name, out var contentValues))
				return contentValues.FirstOrDefault();
			return null;
		}

		private static string TransportError(Exception ex)
		{
			var detail = ProtectedWorkflowLogText.Error(ProtectedWorkflowErrorCodes.HttpFailed, ex);
			if (ex is HttpRequestException requestException)
				detail += $" ({requestException.HttpRequestError})";
			if (ex.InnerException is System.Net.Sockets.SocketException socketException)
				detail += $" ({socketException.SocketErrorCode})";
			return detail;
		}

		/// <summary>"HTTP 204 No Content" from the status code alone — never the server-supplied reason text.</summary>
		public static string StatusLine(int status)
		{
			var name = Enum.IsDefined(typeof(HttpStatusCode), status) ? ((HttpStatusCode)status).ToString() : null;
			return name == null ? $"HTTP {status}" : $"HTTP {status} {Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ")}";
		}

		private static MediaTypeHeaderValue ParseContentType(string contentType)
		{
			if (string.IsNullOrWhiteSpace(contentType) || !MediaTypeHeaderValue.TryParse(contentType, out var parsed))
				parsed = new MediaTypeHeaderValue("application/json");
			if (string.IsNullOrWhiteSpace(parsed.CharSet))
				parsed.CharSet = "utf-8";
			return parsed;
		}

		internal static HttpMessageHandler CreateHandler(bool protectedMode)
		{
			if (!protectedMode)
				return new HttpClientHandler();

			return new SocketsHttpHandler
			{
				AllowAutoRedirect = false,
				UseCookies = false,
				ConnectTimeout = TimeSpan.FromSeconds(Math.Max(1, DataProtectionConfig.ProtectedWorkflowHttpTimeoutSeconds)),
				SslOptions = new SslClientAuthenticationOptions
				{
					EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
				}
			};
		}

		private static HttpCredential ReadCredential(WorkflowActionContext context)
		{
			if (string.IsNullOrWhiteSpace(context.DecryptedCredentialJson))
				return null;
			try
			{
				return JsonConvert.DeserializeObject<HttpCredential>(context.DecryptedCredentialJson);
			}
			catch (JsonException)
			{
				return null;
			}
		}

		/// <summary>
		/// The auth scheme: the credential's explicit authType when present, otherwise the credential TYPE — the web
		/// credential editor stores only the type's fields ({ token }, { username, password }, { headerName, apiKey }).
		/// </summary>
		public static string ResolveAuthType(HttpCredential cred, int? credentialType)
		{
			if (cred == null)
				return null;

			var explicitType = cred.AuthType?.Trim().ToLowerInvariant();
			if (!string.IsNullOrEmpty(explicitType))
				return explicitType == "oauth2clientcredentials" || explicitType == "client_credentials" ? "oauth2" : explicitType;

			return credentialType switch
			{
				(int)WorkflowCredentialType.HttpBearer => "bearer",
				(int)WorkflowCredentialType.HttpBasic => "basic",
				(int)WorkflowCredentialType.HttpApiKey => "apikey",
				(int)WorkflowCredentialType.OAuth2ClientCredentials => "oauth2",
				_ => null
			};
		}

		private static void ApplyAuth(HttpRequestHeaders headers, HttpCredential cred, string authType)
		{
			if (cred == null) return;
			switch (authType)
			{
				case "bearer":
					headers.Authorization = new AuthenticationHeaderValue("Bearer", cred.Token);
					break;
				case "basic":
					var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cred.Username}:{cred.Password}"));
					headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
					break;
				case "apikey":
					headers.TryAddWithoutValidation(cred.ApiKeyHeader ?? cred.HeaderName ?? "X-Api-Key", cred.ApiKey);
					break;
			}
		}

		private sealed class CachedToken
		{
			public string AccessToken { get; init; }
			public DateTime RefreshAfterUtc { get; init; }
		}

		private readonly struct TokenResult
		{
			public TokenResult(string accessToken, int? status, bool redirected)
			{
				AccessToken = accessToken;
				Status = status;
				Redirected = redirected;
			}

			public string AccessToken { get; }
			public int? Status { get; }
			public bool Redirected { get; }
		}

		/// <summary>OAuth2 client credentials grant, cached until 60 seconds before the token expires. The token and secret are never logged.</summary>
		private static async Task<TokenResult> GetOAuthTokenAsync(HttpClient client, HttpCredential cred, CancellationToken cancellationToken)
		{
			var cacheKey = TokenCacheKey(cred);
			if (TokenCache.TryGetValue(cacheKey, out var cached) && cached.RefreshAfterUtc > DateTime.UtcNow)
				return new TokenResult(cached.AccessToken, null, false);

			var form = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("grant_type", "client_credentials") };
			if (cred.IsPrivateKeyJwt)
			{
				// SMART Backend Services: a short-lived assertion signed with the credential's current key (kid in the header).
				form.Add(new KeyValuePair<string, string>("client_assertion_type", WorkflowJwtKeys.AssertionType));
				form.Add(new KeyValuePair<string, string>("client_assertion",
					WorkflowJwtKeys.CreateAssertion(WorkflowJwtKeys.Current(cred.SigningKeys), cred.ClientId, cred.TokenUrl, DateTime.UtcNow)));
			}
			else
			{
				form.Add(new KeyValuePair<string, string>("client_id", cred.ClientId));
				form.Add(new KeyValuePair<string, string>("client_secret", cred.ClientSecret));
			}
			if (!string.IsNullOrWhiteSpace(cred.Scope))
				form.Add(new KeyValuePair<string, string>("scope", cred.Scope));
			if (!string.IsNullOrWhiteSpace(cred.Audience))
				form.Add(new KeyValuePair<string, string>("audience", cred.Audience));

			using var request = new HttpRequestMessage(HttpMethod.Post, cred.TokenUrl) { Content = new FormUrlEncodedContent(form) };
			using var response = await client.SendAsync(request, cancellationToken);
			var status = (int)response.StatusCode;
			if (status >= 300 && status < 400)
				return new TokenResult(null, status, true);
			if (!response.IsSuccessStatusCode)
				return new TokenResult(null, status, false);

			string accessToken;
			int expiresIn;
			try
			{
				var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
				accessToken = json.Value<string>("access_token");
				expiresIn = json["expires_in"] == null ? 0 : (int)Math.Min(int.MaxValue, json["expires_in"].Value<double>());
			}
			catch (Exception)
			{
				return new TokenResult(null, status, false);
			}

			if (string.IsNullOrWhiteSpace(accessToken))
				return new TokenResult(null, status, false);

			var lifetime = TimeSpan.FromSeconds(expiresIn);
			if (lifetime > TokenRefreshMargin)
				TokenCache[cacheKey] = new CachedToken { AccessToken = accessToken, RefreshAfterUtc = DateTime.UtcNow + lifetime - TokenRefreshMargin };

			return new TokenResult(accessToken, status, false);
		}

		private static string TokenCacheKey(HttpCredential cred)
		{
			var secret = cred.IsPrivateKeyJwt ? "jwt:" + WorkflowJwtKeys.Current(cred.SigningKeys)?.Kid : cred.ClientSecret;
			var material = string.Join("\n", cred.TokenUrl, cred.ClientId, secret, cred.Scope, cred.Audience);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
		}

		/// <summary>Test seam: forgets every cached OAuth2 token.</summary>
		internal static void ClearTokenCache() => TokenCache.Clear();
	}

	public class HttpActionConfig
	{
		public string Url { get; set; }
		public string ContentType { get; set; }
		public int TimeoutSeconds { get; set; } = 30;
		public System.Collections.Generic.Dictionary<string, string> Headers { get; set; }

		/// <summary>Header that carries run.idempotency_key (for example Idempotency-Key).</summary>
		public string IdempotencyHeader { get; set; }

		/// <summary>FHIR conditional create: sent as If-None-Exist.</summary>
		public string IfNoneExist { get; set; }
	}

	public class HttpCredential
	{
		public string AuthType { get; set; }
		public string Token { get; set; }
		public string Username { get; set; }
		public string Password { get; set; }
		public string ApiKeyHeader { get; set; }

		/// <summary>The web credential editor's name for <see cref="ApiKeyHeader"/>.</summary>
		public string HeaderName { get; set; }

		public string ApiKey { get; set; }

		// OAuth2 client credentials
		public string TokenUrl { get; set; }
		public string ClientId { get; set; }
		public string ClientSecret { get; set; }
		public string Scope { get; set; }
		public string Audience { get; set; }

		/// <summary>client_secret (default) or private_key_jwt.</summary>
		public string AuthMethod { get; set; }

		/// <summary>private_key_jwt signing keys (inside the encrypted credential only).</summary>
		public List<WorkflowSigningKey> SigningKeys { get; set; }

		[JsonIgnore]
		public bool IsPrivateKeyJwt => WorkflowJwtKeys.NormalizeAuthMethod(AuthMethod) == WorkflowJwtKeys.PrivateKeyJwt;
	}
}
