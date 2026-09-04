using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Neris
{
	/// <summary>
	/// The NERIS HTTP boundary generated against the pinned contract (Contract/neris-openapi-v1.4.78): POST /token
	/// (form, grant_type), POST /incident/{entity}/validate (204 valid, 422 issues), POST /incident/{entity}
	/// (201 IncidentCreatedResponse), PUT /incident/{entity}/{id}, GET /incident/{entity}/{id}. Bearer tokens are
	/// cached per profile until shortly before expiry. Every reply is reduced to a NerisSubmissionOutcome; the
	/// verbatim body rides in ResponseJson for the artifact and never anywhere else.
	/// </summary>
	public class NerisApiClient : INerisApiClient
	{
		private static readonly HttpClient SharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(5, NerisConfig.TimeoutSeconds)) };
		private static readonly ConcurrentDictionary<string, CachedToken> Tokens = new ConcurrentDictionary<string, CachedToken>();

		private readonly HttpClient _http;

		public NerisApiClient() : this(SharedHttpClient) { }

		/// <summary>Test seam: a client over a fake handler.</summary>
		public NerisApiClient(HttpClient http)
		{
			_http = http ?? throw new ArgumentNullException(nameof(http));
		}

		public static string BaseUrlFor(RmsNerisProfile profile)
		{
			if (!string.IsNullOrWhiteSpace(profile?.BaseUrlOverride))
				return profile.BaseUrlOverride.TrimEnd('/');
			if (profile?.Environment == NerisEnvironments.Sandbox && !string.IsNullOrWhiteSpace(NerisConfig.SandboxBaseUrl))
				return NerisConfig.SandboxBaseUrl.TrimEnd('/');
			return (NerisConfig.BaseUrl ?? string.Empty).TrimEnd('/');
		}

		public Task<NerisSubmissionOutcome> ValidateAsync(RmsNerisProfile profile, NerisCredential credential, string payloadJson, CancellationToken cancellationToken = default)
		{
			return SendAsync(profile, credential, HttpMethod.Post, () => $"/incident/{Entity(profile)}/validate", payloadJson, ValidateOutcome, cancellationToken);
		}

		public Task<NerisSubmissionOutcome> CreateIncidentAsync(RmsNerisProfile profile, NerisCredential credential, string payloadJson, CancellationToken cancellationToken = default)
		{
			return SendAsync(profile, credential, HttpMethod.Post, () => $"/incident/{Entity(profile)}", payloadJson, CreateOutcome, cancellationToken);
		}

		public Task<NerisSubmissionOutcome> UpdateIncidentAsync(RmsNerisProfile profile, NerisCredential credential, string nerisIncidentId, string payloadJson, CancellationToken cancellationToken = default)
		{
			return SendAsync(profile, credential, HttpMethod.Put, () => $"/incident/{Entity(profile)}/{Uri.EscapeDataString(nerisIncidentId ?? string.Empty)}", payloadJson, UpdateOutcome(nerisIncidentId), cancellationToken);
		}

		public Task<NerisSubmissionOutcome> GetStatusAsync(RmsNerisProfile profile, NerisCredential credential, string nerisIncidentId, CancellationToken cancellationToken = default)
		{
			return SendAsync(profile, credential, HttpMethod.Get, () => $"/incident/{Entity(profile)}/{Uri.EscapeDataString(nerisIncidentId ?? string.Empty)}", null, StatusOutcome(nerisIncidentId), cancellationToken);
		}

		private static string Entity(RmsNerisProfile profile) => Uri.EscapeDataString(profile.NerisEntityId);

		private async Task<NerisSubmissionOutcome> SendAsync(RmsNerisProfile profile, NerisCredential credential, HttpMethod method, Func<string> pathFactory, string body,
			Func<HttpStatusCode, string, NerisSubmissionOutcome> interpret, CancellationToken cancellationToken)
		{
			if (profile == null || string.IsNullOrWhiteSpace(profile.NerisEntityId))
				return Fatal("The department has no NERIS entity ID configured.");
			if (credential == null)
				return Fatal("The department has no NERIS credential configured.");

			var path = pathFactory();

			string token;
			try
			{
				token = await GetTokenAsync(profile, credential, cancellationToken);
			}
			catch (NerisAuthException ex)
			{
				return Fatal(ex.Message);
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
			{
				return Transient("NERIS token endpoint unreachable: " + ex.Message);
			}

			try
			{
				using var request = new HttpRequestMessage(method, BaseUrlFor(profile) + path);
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
				request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
				if (body != null)
					request.Content = new StringContent(body, Encoding.UTF8, "application/json");

				using var response = await _http.SendAsync(request, cancellationToken);
				var text = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);

				if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
				{
					Tokens.TryRemove(TokenKey(profile), out _);
					return WithStatus(Fatal($"NERIS refused the credential ({(int)response.StatusCode})."), response.StatusCode, text);
				}

				if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
					return WithStatus(Transient($"NERIS returned {(int)response.StatusCode}."), response.StatusCode, text);

				return interpret(response.StatusCode, text);
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
			{
				return Transient("NERIS unreachable: " + ex.Message);
			}
		}

		private static NerisSubmissionOutcome ValidateOutcome(HttpStatusCode status, string text)
		{
			if (status == HttpStatusCode.NoContent || status == HttpStatusCode.OK)
				return WithStatus(new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Accepted, Message = "Valid" }, status, text);
			if ((int)status == 422)
				return WithStatus(Rejected(text), status, text);
			return WithStatus(Transient($"Unexpected NERIS validate reply {(int)status}."), status, text);
		}

		private static NerisSubmissionOutcome CreateOutcome(HttpStatusCode status, string text)
		{
			if (status == HttpStatusCode.Created || status == HttpStatusCode.OK)
			{
				var json = TryParse(text);
				var outcome = new NerisSubmissionOutcome
				{
					Kind = NerisOutcomeKind.Created,
					ExternalId = (string)json?["neris_id"],
					ExternalStatus = (string)json?["incident_status"]?["status"]
				};
				return WithStatus(Promote(outcome), status, text);
			}
			if ((int)status == 422)
				return WithStatus(Rejected(text), status, text);
			return WithStatus(Transient($"Unexpected NERIS create reply {(int)status}."), status, text);
		}

		private static Func<HttpStatusCode, string, NerisSubmissionOutcome> UpdateOutcome(string nerisIncidentId)
		{
			return (status, text) =>
			{
				if (status == HttpStatusCode.OK || status == HttpStatusCode.NoContent)
					return WithStatus(new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Updated, ExternalId = nerisIncidentId }, status, text);
				if ((int)status == 422)
					return WithStatus(Rejected(text), status, text);
				if (status == HttpStatusCode.NotFound)
					return WithStatus(Fatal("NERIS no longer knows the incident; the next submission must create it again."), status, text);
				return WithStatus(Transient($"Unexpected NERIS update reply {(int)status}."), status, text);
			};
		}

		private static Func<HttpStatusCode, string, NerisSubmissionOutcome> StatusOutcome(string nerisIncidentId)
		{
			return (status, text) =>
			{
				if (status != HttpStatusCode.OK)
					return WithStatus(Transient($"Unexpected NERIS status reply {(int)status}."), status, text);

				var json = TryParse(text);
				var outcome = new NerisSubmissionOutcome { ExternalId = nerisIncidentId, ExternalStatus = (string)json?["incident_status"]?["status"], Kind = NerisOutcomeKind.Pending };
				return WithStatus(Promote(outcome), status, text);
			};
		}

		/// <summary>NERIS incident status values: APPROVED, REJECTED, FAILED, SUBMITTED, PENDING_APPROVAL, PENDING_INCIDENT_DATA, DELETED.</summary>
		public static NerisSubmissionOutcome Promote(NerisSubmissionOutcome outcome)
		{
			switch ((outcome.ExternalStatus ?? string.Empty).ToUpperInvariant())
			{
				case "APPROVED":
					outcome.Kind = NerisOutcomeKind.Accepted;
					break;
				case "REJECTED":
					outcome.Kind = NerisOutcomeKind.Rejected;
					if (outcome.Errors.Count == 0)
						outcome.Errors.Add(new NerisSubmissionError { Code = "REJECTED", Message = "The destination rejected the incident." });
					break;
				case "FAILED":
					outcome.Kind = NerisOutcomeKind.Fatal;
					outcome.Message ??= "The destination marked the incident FAILED.";
					break;
				case "DELETED":
					outcome.Kind = NerisOutcomeKind.Fatal;
					outcome.Message ??= "The destination deleted the incident.";
					break;
				default:
					if (outcome.Kind == NerisOutcomeKind.Created && !string.IsNullOrEmpty(outcome.ExternalId))
						break; // created; status pending is expressed by the worker as AwaitingDestination
					if (outcome.Kind != NerisOutcomeKind.Updated)
						outcome.Kind = NerisOutcomeKind.Pending;
					break;
			}
			return outcome;
		}

		/// <summary>Reduces an HTTPValidationError body to codes and field paths; message text is kept short and never echoes payload values.</summary>
		public static NerisSubmissionOutcome Rejected(string text)
		{
			var outcome = new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Rejected };
			var json = TryParse(text);
			var details = json?["detail"] as JArray;
			if (details != null)
			{
				foreach (var detail in details.OfType<JObject>())
				{
					var loc = detail["loc"] as JArray;
					outcome.Errors.Add(new NerisSubmissionError
					{
						Code = ((string)detail["type"] ?? "validation").Trim(),
						FieldPath = loc == null ? null : string.Join(".", loc.Select(l => (string)l).Where(l => !string.IsNullOrEmpty(l) && l != "body")),
						Message = Truncate((string)detail["msg"], 300)
					});
				}
			}
			else if (json?["detail"] != null)
			{
				outcome.Errors.Add(new NerisSubmissionError { Code = "validation", Message = Truncate(json["detail"].ToString(), 300) });
			}

			if (outcome.Errors.Count == 0)
				outcome.Errors.Add(new NerisSubmissionError { Code = "validation", Message = "The destination rejected the payload." });

			outcome.Message = $"{outcome.Errors.Count} validation issue(s).";
			return outcome;
		}

		private async Task<string> GetTokenAsync(RmsNerisProfile profile, NerisCredential credential, CancellationToken cancellationToken)
		{
			var key = TokenKey(profile);
			if (Tokens.TryGetValue(key, out var cached) && cached.ExpiresOn > DateTime.UtcNow.AddSeconds(60))
				return cached.AccessToken;

			var form = new Dictionary<string, string> { ["grant_type"] = profile.GrantType == NerisGrantTypes.Password ? "password" : "client_credentials" };
			if (profile.GrantType == NerisGrantTypes.Password)
			{
				form["username"] = credential.Username ?? string.Empty;
				form["password"] = credential.Password ?? string.Empty;
			}
			else
			{
				form["username"] = credential.ClientId ?? string.Empty;
				form["password"] = credential.ClientSecret ?? string.Empty;
			}

			using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrlFor(profile) + "/token") { Content = new FormUrlEncodedContent(form) };
			using var response = await _http.SendAsync(request, cancellationToken);
			var text = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);
			var json = TryParse(text);

			if (response.StatusCode == HttpStatusCode.OK && json?["access_token"] != null)
			{
				var expires = json["expires_in"]?.Type == JTokenType.Integer ? (int)json["expires_in"] : 3600;
				var token = new CachedToken { AccessToken = (string)json["access_token"], ExpiresOn = DateTime.UtcNow.AddSeconds(expires) };
				Tokens[key] = token;
				return token.AccessToken;
			}

			if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
				throw new HttpRequestException($"NERIS token endpoint returned {(int)response.StatusCode}.");

			var error = (string)json?["error"] ?? (string)json?["challenge_name"] ?? response.StatusCode.ToString();
			throw new NerisAuthException($"NERIS did not issue a token ({error}).");
		}

		private static string TokenKey(RmsNerisProfile profile) => $"{profile.DepartmentId}:{profile.RmsNerisProfileId}:{profile.RowVersion}";

		private static NerisSubmissionOutcome WithStatus(NerisSubmissionOutcome outcome, HttpStatusCode status, string text)
		{
			outcome.StatusCode = (int)status;
			outcome.ResponseJson = text;
			return outcome;
		}

		private static NerisSubmissionOutcome Fatal(string message) => new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Fatal, Message = message };
		private static NerisSubmissionOutcome Transient(string message) => new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Transient, Message = message };

		private static JObject TryParse(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return null;
			try { return JObject.Parse(text); }
			catch { return null; }
		}

		private static string Truncate(string value, int max) => value == null ? null : value.Length <= max ? value : value.Substring(0, max);

		private sealed class CachedToken
		{
			public string AccessToken { get; set; }
			public DateTime ExpiresOn { get; set; }
		}

		private sealed class NerisAuthException : Exception
		{
			public NerisAuthException(string message) : base(message) { }
		}
	}
}
