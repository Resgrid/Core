using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Novu.Domain.Models.Subscribers;
using RabbitMQ.Client;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus.Models;
using SharpCompress.Common;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;


namespace Resgrid.Providers.Messaging
{
	public class NovuProvider : INovuProvider
	{
		private const int MaxLoggedErrorBodyLength = 500;

		/// <summary>
		/// Anything long and opaque enough to be a credential rather than prose. Device tokens are the
		/// reason this exists: a rejected credential write echoes the token back inside its validation
		/// message, and FCM/APNS tokens are well over this length.
		/// </summary>
		private static readonly Regex OpaqueValuePattern = new Regex(@"[A-Za-z0-9_\-:\.]{20,}", RegexOptions.Compiled);

		private static readonly Regex WhitespaceRunPattern = new Regex(@"\s+", RegexOptions.Compiled);

		/// <summary>
		/// Novu's error bodies are provider-controlled and unbounded, and the calls that produce them
		/// carry device tokens and notification wording -- a validation failure echoes the rejected
		/// payload straight back. Rather than trusting the body, pull the few diagnostic fields worth
		/// having, redact anything token-shaped inside them and cap the result. A body that isn't the
		/// shape we expect is reported by size alone; its content never reaches the log.
		/// </summary>
		private static string DescribeErrorBody(string body)
		{
			if (string.IsNullOrWhiteSpace(body))
				return "<no body>";

			var summary = ExtractDiagnosticFields(body);
			if (string.IsNullOrWhiteSpace(summary))
				return $"<unrecognized body, {body.Length} chars>";

			summary = WhitespaceRunPattern.Replace(summary, " ").Trim();
			summary = OpaqueValuePattern.Replace(summary, "<redacted>");

			return summary.Length > MaxLoggedErrorBodyLength
				? summary.Substring(0, MaxLoggedErrorBodyLength) + "..."
				: summary;
		}

		/// <summary>
		/// Whitelist, not blacklist: only these keys are ever read out of the body, so a field we have
		/// not vetted cannot reach the log by being added on Novu's side.
		/// </summary>
		private static string? ExtractDiagnosticFields(string body)
		{
			try
			{
				var parsed = JToken.Parse(body);
				var parts = new List<string>();

				foreach (var name in new[] { "statusCode", "error", "message" })
				{
					var value = parsed.SelectToken(name);
					if (value == null)
						continue;

					// class-validator returns message as an array of strings.
					if (value.Type == JTokenType.Array)
					{
						var items = value.Children()
							.Where(x => x.Type != JTokenType.Object && x.Type != JTokenType.Array)
							.Select(x => x.ToString());

						var joined = string.Join("; ", items);
						if (!string.IsNullOrWhiteSpace(joined))
							parts.Add($"{name}={joined}");
					}
					else if (value.Type != JTokenType.Object)
					{
						parts.Add($"{name}={value}");
					}
				}

				var errors = parsed.SelectToken("errors");
				if (errors != null)
				{
					var described = DescribeValidationErrors(errors);
					if (!string.IsNullOrWhiteSpace(described))
						parts.Add($"errors={described}");
				}

				return string.Join(" ", parts);
			}
			catch (JsonException)
			{
				return null;
			}
		}

		/// <summary>
		/// Both validation-error shapes Novu emits are handled: class-validator entries
		/// ({ property, constraints }) and zod issues ({ path, message }), plus the v2 keyed form
		/// ({ field: { messages: [...] } }). Only field names and rule messages are read out --
		/// the rejected values themselves are never pulled into the log.
		/// </summary>
		private static string DescribeValidationErrors(JToken errors)
		{
			var items = new List<string>();

			if (errors.Type == JTokenType.Object)
			{
				foreach (var prop in ((JObject)errors).Properties())
				{
					var messages = ExtractErrorMessages(prop.Value);
					items.Add(messages.Count > 0 ? $"{prop.Name}: {string.Join("; ", messages)}" : prop.Name);
				}
			}
			else if (errors.Type == JTokenType.Array)
			{
				foreach (var err in errors.Children())
				{
					if (err.Type != JTokenType.Object)
						continue;

					var label = err.SelectToken("property")?.ToString();
					if (label == null)
					{
						var path = err.SelectToken("path");
						if (path != null)
							label = path.Type == JTokenType.Array
								? string.Join(".", path.Children().Select(p => p.ToString()))
								: path.ToString();
					}

					var messages = new List<string>();
					var message = err.SelectToken("message");
					if (message != null && message.Type == JTokenType.String)
						messages.Add(message.ToString());

					if (err.SelectToken("constraints") is JObject constraints)
						messages.AddRange(constraints.Properties().Select(p => p.Value.ToString()));

					if (label == null && messages.Count == 0)
						continue;

					items.Add(messages.Count > 0 ? $"{label ?? "?"}: {string.Join("; ", messages)}" : label);
				}
			}

			return string.Join(" | ", items);
		}

		private static List<string> ExtractErrorMessages(JToken value)
		{
			var result = new List<string>();

			if (value.Type == JTokenType.String)
				result.Add(value.ToString());
			else if (value.Type == JTokenType.Array)
				result.AddRange(value.Children().Where(x => x.Type == JTokenType.String).Select(x => x.ToString()));
			else if (value.Type == JTokenType.Object && value.SelectToken("messages") is JToken messages && messages.Type == JTokenType.Array)
				result.AddRange(messages.Children().Where(x => x.Type == JTokenType.String).Select(x => x.ToString()));

			return result;
		}

		private static string NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

		/// <summary>
		/// Every Novu call goes through the shared retry (see <see cref="NovuRequestRetry"/>), with each retried
		/// attempt logged as a warning naming what was being sent. <paramref name="send"/> must build a fresh
		/// request each call.
		/// </summary>
		private static Task<NovuRequestResult> SendWithRetryAsync(Func<Task<HttpResponseMessage>> send, string operation)
		{
			return NovuRequestRetry.Shared.SendAsync(send,
				(attempt, failure) => Logging.LogWarning($"Novu {operation} attempt {attempt} failed ({failure}); retrying."));
		}

		private async Task<bool> CreateSubscriber(string id, int departmentId, string email, string firstName, string lastName, List<AdditionalData> data)
		{
			try
			{
				using (var httpClient = new HttpClient())
				{
					var requestUrl = $"{ChatConfig.NovuBackendUrl}/v2/subscribers";
					httpClient.DefaultRequestHeaders.Add("idempotency-key", Guid.NewGuid().ToString());
					httpClient.DefaultRequestHeaders.Add("Authorization", $"ApiKey {ChatConfig.NovuSecretKey}");

					// The v2 endpoint validates optional fields (@IsEmail, @IsTimeZone, @IsLocale) whenever
					// they are present -- @IsOptional only skips null/undefined, so an empty string is a 422,
					// not an omission. Empty values must therefore be null (dropped by NullValueHandling.Ignore)
					// and the always-empty phone/avatar/timezone/locale keys are not sent at all.
					var payload = new
					{
						subscriberId = id,
						firstName = NullIfWhiteSpace(firstName),
						lastName = NullIfWhiteSpace(lastName),
						email = NullIfWhiteSpace(email),
						data = new Dictionary<string, object>()
					};

					payload.data.Add("DepartmentId", departmentId);

					if (data != null)
					{
						foreach (var item in data)
						{
							payload.data.Add(item.Key, item.Value);
						}
					}

					string jsonContent = JsonConvert.SerializeObject(payload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

					var create = await SendWithRetryAsync(
						() => httpClient.PostAsync(requestUrl, new StringContent(jsonContent, Encoding.UTF8, "application/json")),
						$"subscriber create for '{id}'");

					using var response = create.Response;

					// A rejected create left the subscriber missing entirely, and every later credential
					// write and trigger against it fails for a reason that looks unrelated. Only the 409 is
					// expected: re-registering a device re-runs this, and Novu answers an already-present
					// subscriber with a conflict, which is the steady state rather than a fault. That also
					// covers a retry after an attempt that created the subscriber but lost its answer.
					if (!response.IsSuccessStatusCode)
					{
						if (response.StatusCode == HttpStatusCode.Conflict)
							return true;

						var error = await response.Content.ReadAsStringAsync();
						Logging.LogError($"Novu subscriber create failed ({(int)response.StatusCode} {response.StatusCode}) after {create.Attempts} attempt(s) subscriber '{id}' department {departmentId}: {DescribeErrorBody(error)}");

						return false;
					}

					return true;
				}
			}
			catch (Exception e)
			{
				Logging.LogException(e, $"Failed to create novu subscriber '{id}'");
				return false;
			}
		}

		public async Task<bool> CreateUserSubscriber(string userId, string code, int departmentId, string email,
			string firstName, string lastName)
		{
			//var data = new List<AdditionalData>();

			//if (!String.IsNullOrWhiteSpace(deviceId))
			//{
			//	data.Add(new AdditionalData
			//	{
			//		Key = "DeviceId",
			//		Value = deviceId
			//	});
			//}

			return await CreateSubscriber($"{code}_User_{userId}", departmentId, email, firstName, lastName, null);
		}

		public async Task<bool> CreateICUserSubscriber(string userId, string code, int departmentId, string email,
			string firstName, string lastName)
		{
			// The IC app gets its own subscriber id so its Novu inbox is separate from the Responder app's.
			return await CreateSubscriber($"{code}_IC_User_{userId}", departmentId, email, firstName, lastName, null);
		}

		public async Task<bool> CreateUnitSubscriber(int unitId, string code, int departmentId, string unitName, string deviceId)
		{
			var data = new List<AdditionalData>();

			if (!String.IsNullOrWhiteSpace(deviceId))
			{
				data.Add(new AdditionalData
				{
					Key = "DeviceId",
					Value = deviceId
				});
			}

			return await CreateSubscriber($"{code}_Unit_{unitId}", departmentId, $"{code}_Unit_{unitId}@units.resgrid.net", unitName, "", data);
		}

		public async Task<bool> DeleteUnitSubscriber(int unitId, string code)
		{
			return await DeleteSubscriber($"{code}_Unit_{unitId}");
		}

		public async Task<bool> DeleteUserSubscriber(string userId, string code)
		{
			return await DeleteSubscriber($"{code}_User_{userId}");
		}

		public async Task<bool> DeleteICUserSubscriber(string userId, string code)
		{
			return await DeleteSubscriber($"{code}_IC_User_{userId}");
		}

		public async Task<List<string>?> ListAllSubscriberIdsAsync(CancellationToken cancellationToken = default)
		{
			// Novu's own page size cap.
			const int limit = 100;
			var ids = new List<string>();

			try
			{
				using (var client = new HttpClient())
				{
					client.DefaultRequestHeaders.Add("Accept", "application/json");
					client.DefaultRequestHeaders.Add("Authorization", $"ApiKey {ChatConfig.NovuSecretKey}");

					for (var page = 0; ; page++)
					{
						cancellationToken.ThrowIfCancellationRequested();

						var read = await SendWithRetryAsync(
							() => client.GetAsync($"{ChatConfig.NovuBackendUrl}/v1/subscribers?page={page}&limit={limit}", cancellationToken),
							$"subscriber list page {page}");

						using var response = read.Response;
						var body = await response.Content.ReadAsStringAsync(cancellationToken);

						if (!response.IsSuccessStatusCode)
						{
							Logging.LogError($"Novu subscriber list failed ({(int)response.StatusCode} {response.StatusCode}) on page {page}: {DescribeErrorBody(body)}");
							return null;
						}

						var (pageIds, hasMore) = NovuSubscriberIds.ReadPage(body, limit);
						if (pageIds == null)
						{
							Logging.LogError($"Novu subscriber list page {page} came back in an unrecognized shape ({body.Length} chars).");
							return null;
						}

						ids.AddRange(pageIds);

						if (!hasMore || pageIds.Count == 0)
							return ids;
					}
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception e)
			{
				Logging.LogException(e, "Failed to list novu subscribers");
				return null;
			}
		}

		/// <summary>
		/// Removes the subscriber and, with it, every device token on its native and web channels. One that never
		/// existed (or is already gone) counts as removed.
		/// </summary>
		private async Task<bool> DeleteSubscriber(string subscriberId)
		{
			// A deployment without Novu has no subscribers to remove.
			if (string.IsNullOrWhiteSpace(ChatConfig.NovuSecretKey))
				return true;

			try
			{
				using (var client = new HttpClient())
				{
					client.DefaultRequestHeaders.Add("Accept", "application/json");
					client.DefaultRequestHeaders.Add("Authorization", $"ApiKey {ChatConfig.NovuSecretKey}");

					var delete = await SendWithRetryAsync(
						() => client.DeleteAsync($"{ChatConfig.NovuBackendUrl}/v1/subscribers/{Uri.EscapeDataString(subscriberId)}"),
						$"subscriber delete for '{subscriberId}'");

					using var response = delete.Response;

					if (response.StatusCode == HttpStatusCode.NotFound || NovuRequestRetry.IsAccepted(delete))
						return true;

					var error = await response.Content.ReadAsStringAsync();
					Logging.LogError($"Novu subscriber delete failed ({(int)response.StatusCode} {response.StatusCode}) after {delete.Attempts} attempt(s) subscriber '{subscriberId}': {DescribeErrorBody(error)}");

					return false;
				}
			}
			catch (Exception e)
			{
				Logging.LogException(e, $"Failed to delete novu subscriber '{subscriberId}'");
				return false;
			}
		}

		private async Task<bool> UpdateSubscriberFcm(string id, string token, string fcmId)
		{
			try
			{
				using (HttpClient client = new HttpClient())
				{
					var url = $"{ChatConfig.NovuBackendUrl}/v1/subscribers/{id}/credentials";
					client.DefaultRequestHeaders.Add("Accept", "application/json");
					client.DefaultRequestHeaders.Add("idempotency-key", Guid.NewGuid().ToString());
					client.DefaultRequestHeaders.Add("Authorization", $"ApiKey {ChatConfig.NovuSecretKey}");

					var payload = new
					{
						providerId = "fcm", //https://github.com/novuhq/novu-ts/blob/main/docs/models/components/chatorpushproviderenum.md
						credentials = new
						{
							deviceTokens = new string[] { token }
						},
						integrationIdentifier = fcmId
					};
					string jsonContent = JsonConvert.SerializeObject(payload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

					var write = await SendWithRetryAsync(
						() => client.PutAsync(url, new StringContent(jsonContent, Encoding.UTF8, "application/json")),
						$"FCM credential write for subscriber '{id}'");

					using var response = write.Response;

					// An unknown integrationIdentifier, an inactive integration or a malformed token all come
					// back as a 4xx here. Swallowing that left the subscriber with no push channel and no clue.
					if (!NovuRequestRetry.IsAccepted(write))
					{
						var error = await response.Content.ReadAsStringAsync();
						Logging.LogError($"Novu FCM credential write failed ({(int)response.StatusCode} {response.StatusCode}) after {write.Attempts} attempt(s) subscriber '{id}' integration '{fcmId}': {DescribeErrorBody(error)}");

						return false;
					}

					return true;
				}
			}
			catch (Exception e)
			{
				Logging.LogException(e, $"Failed to add fcm token to novu subscriber '{id}'");
				return false;
			}
		}

		private async Task<bool> UpdateSubscriberApns(string id, string token, string apnsId, string fcmId)
		{
			try
			{
				using (HttpClient client = new HttpClient())
				{
					var url = $"{ChatConfig.NovuBackendUrl}/v1/subscribers/{id}/credentials";
					client.DefaultRequestHeaders.Add("Accept", "application/json");
					client.DefaultRequestHeaders.Add("idempotency-key", Guid.NewGuid().ToString());
					client.DefaultRequestHeaders.Add("Authorization", $"ApiKey {ChatConfig.NovuSecretKey}");

					string jsonContent = string.Empty;
					if (!string.IsNullOrWhiteSpace(apnsId))
					{
						var payload = new
						{
							providerId = "apns",
							credentials = new
							{
								deviceTokens = new string[] { token }
							},
							integrationIdentifier = apnsId
						};

						jsonContent = JsonConvert.SerializeObject(payload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
					}
					else if (!string.IsNullOrWhiteSpace(fcmId))
					{
						var payload = new
						{
							providerId = "fcm",
							credentials = new
							{
								deviceTokens = new string[] { token }
							},
							integrationIdentifier = fcmId
						};

						jsonContent = JsonConvert.SerializeObject(payload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
					}

					if (string.IsNullOrWhiteSpace(jsonContent))
					{
						Logging.LogWarning($"Novu APNS credential write skipped for subscriber '{id}': neither an apns nor an fcm integration identifier was supplied.");
						return false;
					}

					var write = await SendWithRetryAsync(
						() => client.PutAsync(url, new StringContent(jsonContent, Encoding.UTF8, "application/json")),
						$"APNS credential write for subscriber '{id}'");

					using var response = write.Response;

					if (!NovuRequestRetry.IsAccepted(write))
					{
						var error = await response.Content.ReadAsStringAsync();
						Logging.LogError($"Novu APNS credential write failed ({(int)response.StatusCode} {response.StatusCode}) after {write.Attempts} attempt(s) subscriber '{id}' integration '{apnsId ?? fcmId}': {DescribeErrorBody(error)}");

						return false;
					}

					return true;
				}
			}
			catch (Exception e)
			{
				Logging.LogException(e, $"Failed to add apns token to novu subscriber '{id}'");
				return false;
			}
		}

		#region Inbox

		public async Task<NovuInboxDeleteResult> DeleteUserInboxMessage(string userId, string code, string messageId)
		{
			// The Responder and Dispatch apps share the user subscriber and the IC app has its own. The request
			// doesn't say which app sent it, so the IC inbox is only tried when the message isn't in the user's.
			var result = await DeleteInboxMessage($"{code}_User_{userId}", messageId);
			if (result != NovuInboxDeleteResult.NotFound)
				return result;

			return await DeleteInboxMessage($"{code}_IC_User_{userId}", messageId);
		}

		public Task<NovuInboxDeleteResult> DeleteUnitInboxMessage(int unitId, string code, string messageId)
		{
			return DeleteInboxMessage($"{code}_Unit_{unitId}", messageId);
		}

		/// <summary>
		/// Deletes through Novu's inbox API signed in as the subscriber, never through the environment-wide
		/// DELETE /v1/messages/{id}: that deletes any message in the environment for whoever knows its id, and
		/// message ids are ObjectIds, so a user's own ids lead straight to their neighbours'. An inbox session is
		/// scoped to one subscriber, so Novu itself answers 404 for a message in anyone else's inbox.
		/// </summary>
		private async Task<NovuInboxDeleteResult> DeleteInboxMessage(string subscriberId, string messageId)
		{
			try
			{
				using (var client = new HttpClient())
				{
					// Opening an inbox session creates the subscriber when it doesn't exist, so check first: a delete
					// must not leave an IC subscriber behind for a user who never installed the IC app.
					var lookup = await SendWithRetryAsync(() =>
						{
							var request = new HttpRequestMessage(HttpMethod.Get, $"{ChatConfig.NovuBackendUrl}/v1/subscribers/{Uri.EscapeDataString(subscriberId)}");
							request.Headers.Add("Authorization", $"ApiKey {ChatConfig.NovuSecretKey}");
							return client.SendAsync(request);
						},
						$"subscriber read for '{subscriberId}'");

					using (var response = lookup.Response)
					{
						if (response.StatusCode == HttpStatusCode.NotFound)
							return NovuInboxDeleteResult.NotFound;

						if (!response.IsSuccessStatusCode)
						{
							var error = await response.Content.ReadAsStringAsync();
							Logging.LogError($"Novu subscriber read failed ({(int)response.StatusCode} {response.StatusCode}) after {lookup.Attempts} attempt(s) subscriber '{subscriberId}' while deleting inbox message '{messageId}': {DescribeErrorBody(error)}");

							return NovuInboxDeleteResult.Failed;
						}
					}

					var token = await OpenInboxSession(client, subscriberId);
					if (token == null)
						return NovuInboxDeleteResult.Failed;

					var delete = await SendWithRetryAsync(() =>
						{
							var request = new HttpRequestMessage(HttpMethod.Delete, $"{ChatConfig.NovuBackendUrl}/v1/inbox/notifications/{Uri.EscapeDataString(messageId)}/delete");
							request.Headers.Add("Authorization", $"Bearer {token}");
							return client.SendAsync(request);
						},
						$"inbox message delete for subscriber '{subscriberId}'");

					using (var response = delete.Response)
					{
						if (NovuRequestRetry.IsAccepted(delete))
							return NovuInboxDeleteResult.Deleted;

						// A retry after an attempt that deleted the message but lost its answer also lands here. It is
						// reported as not found rather than deleted: it can't be told apart from a message in someone
						// else's inbox, and reading it as deleted would stop the IC inbox from being tried.
						if (response.StatusCode == HttpStatusCode.NotFound)
							return NovuInboxDeleteResult.NotFound;

						var error = await response.Content.ReadAsStringAsync();
						Logging.LogError($"Novu inbox message delete failed ({(int)response.StatusCode} {response.StatusCode}) after {delete.Attempts} attempt(s) subscriber '{subscriberId}' message '{messageId}': {DescribeErrorBody(error)}");

						return NovuInboxDeleteResult.Failed;
					}
				}
			}
			catch (Exception e)
			{
				Logging.LogException(e, $"Failed to delete inbox message '{messageId}' for novu subscriber '{subscriberId}'");
				return NovuInboxDeleteResult.Failed;
			}
		}

		/// <summary>
		/// Opens an inbox session as the subscriber, the same request the apps' Novu SDK makes, and returns its
		/// bearer token.
		/// </summary>
		private async Task<string?> OpenInboxSession(HttpClient client, string subscriberId)
		{
			var payload = new
			{
				applicationIdentifier = ChatConfig.NovuApplicationId,
				subscriberId = subscriberId,
				// Only checked once HMAC is switched on for the environment; ignored until then.
				subscriberHash = SubscriberHash(subscriberId)
			};

			var jsonContent = JsonConvert.SerializeObject(payload);

			var session = await SendWithRetryAsync(
				() => client.PostAsync($"{ChatConfig.NovuBackendUrl}/v1/inbox/session", new StringContent(jsonContent, Encoding.UTF8, "application/json")),
				$"inbox session for subscriber '{subscriberId}'");

			using var response = session.Response;
			var body = await response.Content.ReadAsStringAsync();

			if (!response.IsSuccessStatusCode)
			{
				Logging.LogError($"Novu inbox session failed ({(int)response.StatusCode} {response.StatusCode}) after {session.Attempts} attempt(s) subscriber '{subscriberId}': {DescribeErrorBody(body)}");
				return null;
			}

			string? token = null;
			try
			{
				var parsed = JToken.Parse(body);
				token = (parsed.SelectToken("data.token") ?? parsed.SelectToken("token"))?.ToString();
			}
			catch (JsonException)
			{
			}

			if (string.IsNullOrWhiteSpace(token))
			{
				Logging.LogError($"Novu inbox session for subscriber '{subscriberId}' came back without a token ({body.Length} chars).");
				return null;
			}

			return token;
		}

		public string? GetUserSubscriberHash(string userId, string code)
		{
			return SubscriberHashIfConfigured($"{code}_User_{userId}");
		}

		public string? GetICUserSubscriberHash(string userId, string code)
		{
			return SubscriberHashIfConfigured($"{code}_IC_User_{userId}");
		}

		public string? GetUnitSubscriberHash(int unitId, string code)
		{
			return SubscriberHashIfConfigured($"{code}_Unit_{unitId}");
		}

		private static string? SubscriberHashIfConfigured(string subscriberId)
		{
			return string.IsNullOrWhiteSpace(ChatConfig.NovuSecretKey) ? null : SubscriberHash(subscriberId);
		}

		/// <summary>
		/// What Novu checks a session against when HMAC is enabled: HMAC-SHA256 of the subscriber id, keyed with
		/// the environment's secret key, as lowercase hex (Novu's createHash). Holding it is what lets a client
		/// open that subscriber's inbox, so it is only ever handed to the subscriber's owner.
		/// </summary>
		private static string SubscriberHash(string subscriberId)
		{
			using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(ChatConfig.NovuSecretKey ?? string.Empty));
			return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(subscriberId))).ToLowerInvariant();
		}

		#endregion Inbox

		public async Task<bool> UpdateUserSubscriberFcm(string userId, string code, string token)
		{
			return await UpdateSubscriberFcm($"{code}_User_{userId}", token, ChatConfig.NovuResponderFcmProviderId);
		}

		public async Task<bool> UpdateUserSubscriberApns(string userId, string code, string token)
		{
			return await UpdateSubscriberApns($"{code}_User_{userId}", token, ChatConfig.NovuResponderApnsProviderId, null);
		}

		public async Task<bool> UpdateICUserSubscriberFcm(string userId, string code, string token)
		{
			return await UpdateSubscriberFcm($"{code}_IC_User_{userId}", token, ChatConfig.NovuICFcmProviderId);
		}

		public async Task<bool> UpdateICUserSubscriberApns(string userId, string code, string token)
		{
			return await UpdateSubscriberApns($"{code}_IC_User_{userId}", token, ChatConfig.NovuICApnsProviderId, null);
		}

		public async Task<bool> UpdateUnitSubscriberFcm(int unitId, string code, string token)
		{
			return await UpdateSubscriberFcm($"{code}_Unit_{unitId}", token, ChatConfig.NovuUnitFcmProviderId);
		}

		public async Task<bool> UpdateUnitSubscriberApns(int unitId, string code, string token)
		{
			//return await UpdateSubscriberApns($"{code}_Unit_{unitId}", token, ChatConfig.NovuUnitApnsProviderId);
			return await UpdateSubscriberApns($"{code}_Unit_{unitId}", token, null, ChatConfig.NovuUnitFcmProviderId);
		}

		#region Web Push (browser and desktop)

		/// <summary>Integration identifier → the integration ids Novu answered with, and when.</summary>
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (HashSet<string> Ids, DateTime FetchedOn)> IntegrationIdsByIdentifier = new();

		private static readonly TimeSpan IntegrationIdLifetime = TimeSpan.FromHours(1);

		/// <summary>
		/// One writer per subscriber within this process. The token list is read, changed and written back,
		/// so two registrations for one person (Core Web and an app signing in together) would otherwise
		/// each write a list missing the other's token. Across processes the loser is restored the next
		/// time its client refreshes its registration.
		/// </summary>
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> SubscriberChannelLocks = new();

		public async Task<bool> AddUserSubscriberWebPushToken(string userId, string code, string token)
		{
			return await ChangeChannelTokens($"{code}_User_{userId}", "fcm", ChatConfig.NovuResponderWebFcmProviderId, token, true, "web push");
		}

		public async Task<bool> RemoveUserSubscriberWebPushToken(string userId, string code, string token)
		{
			return await ChangeChannelTokens($"{code}_User_{userId}", "fcm", ChatConfig.NovuResponderWebFcmProviderId, token, false, "web push");
		}

		public async Task<bool> AddICUserSubscriberWebPushToken(string userId, string code, string token)
		{
			return await ChangeChannelTokens($"{code}_IC_User_{userId}", "fcm", ChatConfig.NovuICWebFcmProviderId, token, true, "web push");
		}

		public async Task<bool> RemoveICUserSubscriberWebPushToken(string userId, string code, string token)
		{
			return await ChangeChannelTokens($"{code}_IC_User_{userId}", "fcm", ChatConfig.NovuICWebFcmProviderId, token, false, "web push");
		}

		public async Task<bool> AddUnitSubscriberWebPushToken(int unitId, string code, string token)
		{
			return await ChangeChannelTokens($"{code}_Unit_{unitId}", "fcm", ChatConfig.NovuUnitWebFcmProviderId, token, true, "web push");
		}

		public async Task<bool> RemoveUnitSubscriberWebPushToken(int unitId, string code, string token)
		{
			return await ChangeChannelTokens($"{code}_Unit_{unitId}", "fcm", ChatConfig.NovuUnitWebFcmProviderId, token, false, "web push");
		}

		#endregion Web Push (browser and desktop)

		#region Native push (iOS and Android)

		public async Task<bool> RemoveUserSubscriberNativeToken(string userId, string code, string token)
		{
			return await RemoveNativeToken($"{code}_User_{userId}", token,
				("fcm", ChatConfig.NovuResponderFcmProviderId), ("apns", ChatConfig.NovuResponderApnsProviderId));
		}

		public async Task<bool> RemoveICUserSubscriberNativeToken(string userId, string code, string token)
		{
			return await RemoveNativeToken($"{code}_IC_User_{userId}", token,
				("fcm", ChatConfig.NovuICFcmProviderId), ("apns", ChatConfig.NovuICApnsProviderId));
		}

		public async Task<bool> RemoveUnitSubscriberNativeToken(int unitId, string code, string token)
		{
			// Unit iOS tokens are written to the unit FCM integration (see UpdateUnitSubscriberApns); the APNs
			// integration is cleaned too in case an older registration went there.
			return await RemoveNativeToken($"{code}_Unit_{unitId}", token,
				("fcm", ChatConfig.NovuUnitFcmProviderId), ("apns", ChatConfig.NovuUnitApnsProviderId));
		}

		/// <summary>
		/// Takes the token off each of the subscriber's native channels. A native registration replaces the
		/// channel with its own token, so the channel holds whichever device registered last: only this token
		/// is removed, and a channel now holding another device's token is left alone. The channel is read,
		/// filtered and written back like the web channel and never deleted by provider, because a provider-wide
		/// credential delete would also take the web channel, which is FCM too.
		/// </summary>
		/// <returns>True when the token is on none of the channels afterwards.</returns>
		private async Task<bool> RemoveNativeToken(string subscriberId, string token, params (string ProviderId, string Identifier)[] channels)
		{
			var removed = true;

			foreach (var (providerId, identifier) in channels)
			{
				// An integration that is not configured never had a token written to it.
				if (string.IsNullOrWhiteSpace(identifier))
					continue;

				removed &= await ChangeChannelTokens(subscriberId, providerId, identifier, token, false, "native push");
			}

			return removed;
		}

		#endregion Native push (iOS and Android)

		/// <summary>
		/// Adds a token to, or removes it from, the subscriber's channel for one integration. The PUT replaces the
		/// channel's token list (Novu's only way to drop a token), so the list is rebuilt from what the channel holds.
		/// </summary>
		private async Task<bool> ChangeChannelTokens(string subscriberId, string providerId, string integrationIdentifier, string token, bool add, string channel)
		{
			if (string.IsNullOrWhiteSpace(token))
				return false;

			if (string.IsNullOrWhiteSpace(integrationIdentifier))
			{
				Logging.LogWarning($"Novu {channel} token {(add ? "add" : "removal")} skipped for subscriber '{subscriberId}': no integration is configured.");
				return false;
			}

			var subscriberLock = SubscriberChannelLocks.GetOrAdd(subscriberId, _ => new SemaphoreSlim(1, 1));
			await subscriberLock.WaitAsync();

			try
			{
				using (var client = new HttpClient())
				{
					client.DefaultRequestHeaders.Add("Accept", "application/json");
					client.DefaultRequestHeaders.Add("Authorization", $"ApiKey {ChatConfig.NovuSecretKey}");

					var integrationIds = await GetIntegrationIds(client, integrationIdentifier, channel);
					if (integrationIds == null)
						return false;

					if (integrationIds.Count == 0)
					{
						// Nothing can be on a channel whose integration does not exist.
						if (!add)
							return true;

						Logging.LogError($"Novu has no integration with identifier '{integrationIdentifier}'; {channel} cannot be registered until it is created (for web push, an FCM integration on the Firebase project the web apps use).");
						return false;
					}

					// A list that could not be read is never written over: the PUT below replaces the channel's
					// tokens, and guessing would sign every other device on it out of push.
					var existing = await GetSubscriberChannelTokens(client, subscriberId, integrationIds, channel);
					if (existing == null)
						return false;

					var updated = add
						? NovuWebPushTokens.Add(existing, token, WebPushConfig.MaxTokensPerSubscriber)
						: NovuWebPushTokens.Remove(existing, token);

					if (updated.SequenceEqual(existing))
						return true;

					var url = $"{ChatConfig.NovuBackendUrl}/v1/subscribers/{subscriberId}/credentials";
					var idempotencyKey = Guid.NewGuid().ToString();

					var payload = new
					{
						providerId = providerId,
						credentials = new
						{
							deviceTokens = updated.ToArray()
						},
						integrationIdentifier = integrationIdentifier
					};

					var jsonContent = JsonConvert.SerializeObject(payload);

					// The key goes on each request rather than the client: the GETs above share this client.
					var write = await SendWithRetryAsync(() =>
						{
							var request = new HttpRequestMessage(HttpMethod.Put, url);
							request.Headers.Add("idempotency-key", idempotencyKey);
							request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
							return client.SendAsync(request);
						},
						$"{channel} credential write for subscriber '{subscriberId}'");

					using var response = write.Response;

					if (!NovuRequestRetry.IsAccepted(write))
					{
						var error = await response.Content.ReadAsStringAsync();
						Logging.LogError($"Novu {channel} credential write failed ({(int)response.StatusCode} {response.StatusCode}) after {write.Attempts} attempt(s) subscriber '{subscriberId}' integration '{integrationIdentifier}' ({(add ? "add" : "removal")}, {updated.Count} tokens): {DescribeErrorBody(error)}");

						return false;
					}

					return true;
				}
			}
			catch (Exception e)
			{
				Logging.LogException(e, $"Failed to {(add ? "add" : "remove")} a {channel} token on novu subscriber '{subscriberId}'");
				return false;
			}
			finally
			{
				subscriberLock.Release();
			}
		}

		/// <returns>The ids of the integrations carrying the identifier; empty when Novu has none; null when the list could not be read.</returns>
		private async Task<HashSet<string>?> GetIntegrationIds(HttpClient client, string integrationIdentifier, string channel)
		{
			if (IntegrationIdsByIdentifier.TryGetValue(integrationIdentifier, out var cached) && cached.Ids.Count > 0 &&
				DateTime.UtcNow - cached.FetchedOn < IntegrationIdLifetime)
				return cached.Ids;

			var read = await SendWithRetryAsync(() => client.GetAsync($"{ChatConfig.NovuBackendUrl}/v1/integrations"),
				$"integration list for {channel} integration '{integrationIdentifier}'");

			using var response = read.Response;
			var body = await response.Content.ReadAsStringAsync();

			if (!response.IsSuccessStatusCode)
			{
				Logging.LogError($"Novu integration list failed ({(int)response.StatusCode} {response.StatusCode}) looking up {channel} integration '{integrationIdentifier}': {DescribeErrorBody(body)}");
				return null;
			}

			var ids = NovuWebPushTokens.FindIntegrationIds(body, integrationIdentifier);
			if (ids == null)
			{
				Logging.LogError($"Novu integration list for {channel} integration '{integrationIdentifier}' came back in an unrecognized shape ({body.Length} chars).");
				return null;
			}

			if (ids.Count > 0)
				IntegrationIdsByIdentifier[integrationIdentifier] = (ids, DateTime.UtcNow);

			return ids;
		}

		private async Task<List<string>?> GetSubscriberChannelTokens(HttpClient client, string subscriberId, ISet<string> integrationIds, string channel)
		{
			var read = await SendWithRetryAsync(() => client.GetAsync($"{ChatConfig.NovuBackendUrl}/v1/subscribers/{subscriberId}"),
				$"subscriber read for '{subscriberId}'");

			using var response = read.Response;
			var body = await response.Content.ReadAsStringAsync();

			// No subscriber means no channel: nothing to remove, and an add creates the channel (or reports
			// the missing subscriber itself).
			if (response.StatusCode == HttpStatusCode.NotFound)
				return new List<string>();

			if (!response.IsSuccessStatusCode)
			{
				Logging.LogError($"Novu subscriber read failed ({(int)response.StatusCode} {response.StatusCode}) subscriber '{subscriberId}' while changing {channel} tokens: {DescribeErrorBody(body)}");
				return null;
			}

			var tokens = NovuWebPushTokens.FindChannelTokens(body, integrationIds);
			if (tokens == null)
				Logging.LogError($"Novu subscriber '{subscriberId}' came back in an unrecognized shape ({body.Length} chars); {channel} tokens left unchanged.");

			return tokens;
		}

		private async Task<bool> SendNotification(string title, string body, string recipientId, string eventCode,
			string type, bool enableCustomSounds, int count, string color, string workflowIdentifier, string sound)
		{
			var attempts = 0;

			try
			{
				using (var httpClient = new HttpClient())
				{
					// Set base URL and headers
					httpClient.BaseAddress = new Uri(ChatConfig.NovuBackendUrl);
					httpClient.DefaultRequestHeaders.Add("Authorization", $"ApiKey {ChatConfig.NovuSecretKey}");
					httpClient.DefaultRequestHeaders.Add("idempotency-key", Guid.NewGuid().ToString());

					string channelName = GetAndroidChannelName(eventCode);
					// Build request payload
					var payload = new
					{
						name = workflowIdentifier,
						payload = new
						{
							subject = title,
							body = body,
							eventId = eventCode,
							eventCode = eventCode,
							type = type,
							sound = sound,
						},
						overrides = new
						{
							fcm = new
							{
								android = new
								{
									priority = channelName == "calls" ? "high" : "normal",
									notification = new
									{
										channelId = type,
										defaultSound = true,
										sticky = channelName == "calls" ? true : false,
										//priority = androidChannelName == "calls" ? 5 : 3,
										notification_priority = channelName == "calls" ? "PRIORITY_MAX" : "PRIORITY_DEFAULT",
									},
									data = new
									{
										title = title,
										message = body,
										eventCode = eventCode,
										type = type,
									}
								},
								apns = new
								{
									payload = new
									{
										aps = new
										{
											badge = count,
											sound = new
											{
												name = sound,
												critical = channelName == "calls" ? 1 : 0,
												volume = 1.0f
											},
											category = channelName,
											eventCode = eventCode,
											customType = type
										},
										// expo-notifications on iOS only surfaces the top-level `body` custom key
										// as content.data, and per the APNs spec custom keys belong beside `aps`,
										// not inside it. The aps-nested eventCode/customType above stay for
										// backward compatibility with already deployed apps.
										body = new
										{
											eventCode = eventCode,
											type = type
										},
										eventCode = eventCode,
										type = type
									},
								},
								// Browser and desktop (Electron) tokens on the web channel. Their service worker or
								// main process shows the notification itself and routes a click by eventCode, so
								// everything it needs travels in data (FCM only accepts string values there).
								// The native android/apns blocks above are ignored for these tokens, and this block
								// is ignored for native ones.
								webPush = new
								{
									headers = new Dictionary<string, string>
									{
										["Urgency"] = channelName == "calls" ? "high" : "normal"
									},
									notification = new
									{
										title = title,
										body = body,
										tag = eventCode,
										renotify = true,
										requireInteraction = channelName == "calls"
									},
									data = new Dictionary<string, string>
									{
										["title"] = title ?? string.Empty,
										["message"] = body ?? string.Empty,
										["eventCode"] = eventCode ?? string.Empty,
										["type"] = type ?? string.Empty,
										["category"] = channelName
									}
								},
							},
							apns = new Dictionary<string, object>
							{
								["badge"] = count,
								["sound"] = new
								{
									name = sound,
									critical = channelName == "calls" ? 1 : 0,
									volume = 1.0f
								},
								["type"] = type,
								["category"] = channelName,
								["eventCode"] = eventCode,
								["gcm.message_id"] = "123",
								// node-apn merges `payload` in as the custom data at the top level of the
								// APNs JSON; the nested `body` key is the one expo-notifications on iOS
								// exposes as content.data.
								["payload"] = new Dictionary<string, object>
								{
									["body"] = new
									{
										eventCode = eventCode,
										type = type
									},
									["eventCode"] = eventCode,
									["type"] = type
								},
							},
						},
						to = new[]{ new
					{
						subscriberId = recipientId
					}},
					};

					var payloadString = JsonConvert.SerializeObject(payload);

					// The idempotency-key header above is fixed for this client, so every retry carries the
					// same key as the first attempt.
					var trigger = await SendWithRetryAsync(
						() =>
						{
							attempts++;
							return httpClient.PostAsync("v1/events/trigger", new StringContent(payloadString, Encoding.UTF8, "application/json"));
						},
						$"trigger for workflow '{workflowIdentifier}' subscriber '{recipientId}' event '{eventCode}'");

					using (var result = trigger.Response)
					{
						if (NovuRequestRetry.IsAccepted(trigger))
							return true;

						// A rejected trigger (unknown workflow identifier, unknown subscriber, bad payload) is a
						// 4xx with a body explaining why. Returning the bare bool made every one of those silent,
						// so a workflow that was never created in Novu looked exactly like a delivered push.
						var error = await result.Content.ReadAsStringAsync();
						Logging.LogError($"Novu trigger failed ({(int)result.StatusCode} {result.StatusCode}) after {trigger.Attempts} attempt(s) workflow '{workflowIdentifier}' subscriber '{recipientId}' event '{eventCode}': {DescribeErrorBody(error)}");

						return false;
					}
				}
			}
			catch (Exception e)
			{
				Logging.LogException(e, $"Failed to send novu notification after {attempts} attempt(s) workflow '{workflowIdentifier}' subscriber '{recipientId}' event '{eventCode}'");
				return false;
			}
		}

		public async Task<bool> SendUnitDispatch(string title, string body, int unitId, string depCode, string eventCode, string type, bool enableCustomSounds, int count, string color)
		{
			return await SendNotification(title, body, $"{depCode}_Unit_{unitId}", eventCode, type, enableCustomSounds, count, color, ChatConfig.NovuDispatchUnitWorkflowId, GetSoundFileNameFromType(type));
		}

		public async Task<bool> SendUserDispatch(string title, string body, string userId, string depCode, string eventCode, string type, bool enableCustomSounds, int count, string color)
		{
			return await SendNotification(title, body, $"{depCode}_User_{userId}", eventCode, type, enableCustomSounds, count, color, ChatConfig.NovuDispatchUserWorkflowId, GetSoundFileNameFromType(type));
		}

		public async Task<bool> SendUserMessage(string title, string body, string userId, string depCode, string eventCode, string type)
		{
			return await SendNotification(title, body, $"{depCode}_User_{userId}", eventCode, type, false, 0, null, ChatConfig.NovuMessageUserWorkflowId, GetSoundFileNameFromType(type));
		}

		public async Task<bool> SendUserNotification(string title, string body, string userId, string depCode, string eventCode, string type)
		{
			return await SendNotification(title, body, $"{depCode}_User_{userId}", eventCode, type, false, 0, null, ChatConfig.NovuNotificationUserWorkflowId, GetSoundFileNameFromType(type));
		}

		public async Task<bool> SendICUserNotification(string title, string body, string userId, string depCode, string eventCode, string type)
		{
			return await SendNotification(title, body, $"{depCode}_IC_User_{userId}", eventCode, type, false, 0, null, ChatConfig.NovuNotificationUserWorkflowId, GetSoundFileNameFromType(type));
		}

		public async Task<bool> SendUserChatMessage(string title, string body, string userId, string depCode, string eventCode, string type, int count)
		{
			return await SendNotification(title, body, $"{depCode}_User_{userId}", eventCode, type, false, count, null, ChatConfig.NovuChatWorkflowId, GetSoundFileNameFromType(type));
		}

		public async Task<bool> SendICUserChatMessage(string title, string body, string userId, string depCode, string eventCode, string type, int count)
		{
			return await SendNotification(title, body, $"{depCode}_IC_User_{userId}", eventCode, type, false, count, null, ChatConfig.NovuChatWorkflowId, GetSoundFileNameFromType(type));
		}

		public async Task<bool> SendUnitChatMessage(string title, string body, int unitId, string depCode, string eventCode, string type, int count)
		{
			return await SendNotification(title, body, $"{depCode}_Unit_{unitId}", eventCode, type, false, count, null, ChatConfig.NovuChatWorkflowId, GetSoundFileNameFromType(type));
		}

		#region Private Push Helpers

		private string GetSoundFileNameFromType(string type)
		{
			if (type == ((int)PushSoundTypes.CallEmergency).ToString())
			{
				return "callemergency.wav";
			}
			else if (type == ((int)PushSoundTypes.CallHigh).ToString())
			{
				return "callhigh.wav";
			}
			else if (type == ((int)PushSoundTypes.CallMedium).ToString())
			{
				return "callmedium.wav";
			}
			else if (type == ((int)PushSoundTypes.CallLow).ToString())
			{
				return "calllow.wav";
			}
			else if (type == ((int)PushSoundTypes.Notifiation).ToString())
			{
				return "notification.wav";
			}
			else if (type == ((int)PushSoundTypes.Message).ToString())
			{
				return "message.wav";
			}
			else
			{
				// Modern sound set (PushSoundTypes 7+): uniform stem-based filenames.
				var modernStem = PushSoundFile.GetModernStem(type);
				if (modernStem != null)
					return $"{modernStem}.wav";

				return $"{type}.wav";
			}
		}

		private string FormatForAndroidNativePush(string fileName)
		{
			if (String.IsNullOrWhiteSpace(fileName))
				return string.Empty;

			return Path.GetFileNameWithoutExtension(fileName).Replace("_", "").ToLower();
		}

		private string GetAndroidChannelName(string eventCode)
		{
			string channel = "calls";
			if (eventCode.ToLower().StartsWith("m")) // message
				channel = "messages";
			else if (eventCode.ToLower().StartsWith("c")) //call
				channel = "calls";
			else if (eventCode.ToLower().StartsWith("n")) // notification
				channel = "notifications";
			else if (eventCode.ToLower().StartsWith("t")) // 1 on 1 chat
				channel = "chats";
			else if (eventCode.ToLower().StartsWith("g")) // group chat
				channel = "chats";

			return channel;
		}

		private JObject CreateAndroidNotification(string title, string subTitle, string eventCode, string type,
			int count, string color, string channel)
		{
			if (color == null)
				color = "#ff0000";

			if (count == 0)
				count = 1;

			string soundFilename = FormatForAndroidNativePush(GetSoundFileNameFromType(type));

			dynamic pushNotification = new JObject();

			pushNotification.message = new JObject();
			pushNotification.message.notification = new JObject();
			pushNotification.message.notification.title = title;
			pushNotification.message.notification.body = subTitle;
			//pushNotification.notification.android_channel_id = type;

			pushNotification.message.android = new JObject();

			if (channel != null && channel == "calls")
				pushNotification.message.android.priority = 1;

			//pushNotification.message.android.ttl = "86400";
			pushNotification.message.android.notification = new JObject();
			//pushNotification.android.notification.color = color;
			pushNotification.message.android.notification.channel_id = type;
			//pushNotification.android.notification.sound = soundFilename;
			pushNotification.message.android.notification.default_sound = true;

			if (channel != null && channel == "calls")
			{
				pushNotification.message.android.notification.sticky = true;
				pushNotification.message.android.notification.notification_priority = 5;
			}

			pushNotification.message.data = new JObject();
			pushNotification.message.data.title = title;
			pushNotification.message.data.message = subTitle;
			pushNotification.message.data.eventCode = eventCode;
			pushNotification.message.data.type = type;

			//return pushNotification.ToString();
			return pushNotification;
		}

		private string CreateAppleNotification(string title, string subTitle, string type, int count, string color,
			string eventCode)
		{
			string category = null;

			if (eventCode.ToLower().StartsWith("m")) // message
				category = "messages";
			else if (eventCode.ToLower().StartsWith("c")) //call
				category = "calls";
			else if (eventCode.ToLower().StartsWith("n")) // notification
				category = "notifications";
			else if (eventCode.ToLower().StartsWith("t")) // 1 on 1 chat
				category = "chats";
			else if (eventCode.ToLower().StartsWith("g")) // group chat
				category = "chats";
			else
				category = "notifications";

			var apnsPayload = new ApnsPayload
			{
				aps = new ApnsHeader
				{
					alert = new ApnsAlert
					{
						title = title,
						body = subTitle
					},
					badge = count,
					category = category,
					sound = new ApnsSound
					{
						name = GetSoundFileNameFromType(type),
						critical = category == "calls" ? 1 : 0,
						volume = 1.0f
					}
				},
				eventCode = eventCode,
				type = type,
				body = new ApnsCustomData
				{
					eventCode = eventCode,
					type = type
				}
			};

			var appleNotification = JsonConvert.SerializeObject(apnsPayload);

			return appleNotification;
		}

		#endregion Private Push Helpers
	}
}
