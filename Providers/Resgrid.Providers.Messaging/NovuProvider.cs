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

				return string.Join(" ", parts);
			}
			catch (JsonException)
			{
				return null;
			}
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

					var payload = new
					{
						subscriberId = id,
						firstName = firstName,
						lastName = lastName,
						email = email,
						phone = "",
						avatar = "",
						timezone = "",
						locale = "",
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

					var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

					var response = await httpClient.PostAsync(requestUrl, content);

					return response.IsSuccessStatusCode;
				}
			}
			catch (Exception e)
			{
				Logging.LogException(e, "Failed to create novu subscriber");
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

		private async Task<bool> UpdateSubscriberFcm(string id, string token, string fcmId)
		{
			try
			{
				using (HttpClient client = new HttpClient())
				{
					var url = $"{ChatConfig.NovuBackendUrl}/v1/subscribers/{id}/credentials";
					var request = new HttpRequestMessage(HttpMethod.Put, url);
					request.Headers.Add("Accept", "application/json");
					request.Headers.Add("idempotency-key", Guid.NewGuid().ToString());
					request.Headers.Add("Authorization", $"ApiKey {ChatConfig.NovuSecretKey}");

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

					request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
					HttpResponseMessage response = await client.SendAsync(request);

					// An unknown integrationIdentifier, an inactive integration or a malformed token all come
					// back as a 4xx here. Swallowing that left the subscriber with no push channel and no clue.
					if (!response.IsSuccessStatusCode)
					{
						var error = await response.Content.ReadAsStringAsync();
						Logging.LogError($"Novu FCM credential write failed ({(int)response.StatusCode} {response.StatusCode}) subscriber '{id}' integration '{fcmId}': {DescribeErrorBody(error)}");

						return false;
					}

					return true;
				}
			}
			catch (Exception e)
			{
				Logging.LogException(e, "Failed to add fcm token to novu subscriber");
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
					var request = new HttpRequestMessage(HttpMethod.Put, url);
					request.Headers.Add("Accept", "application/json");
					request.Headers.Add("idempotency-key", Guid.NewGuid().ToString());
					request.Headers.Add("Authorization", $"ApiKey {ChatConfig.NovuSecretKey}");

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

					request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
					HttpResponseMessage response = await client.SendAsync(request);

					if (!response.IsSuccessStatusCode)
					{
						var error = await response.Content.ReadAsStringAsync();
						Logging.LogError($"Novu APNS credential write failed ({(int)response.StatusCode} {response.StatusCode}) subscriber '{id}' integration '{apnsId ?? fcmId}': {DescribeErrorBody(error)}");

						return false;
					}

					return true;
				}
			}
			catch (Exception e)
			{
				Logging.LogException(e, "Failed to add apns token to novu subscriber");
				return false;
			}
		}

		public async Task<bool> DeleteMessage(string messageId)
		{
			try
			{
				using (var httpClient = new HttpClient())
				{
					var requestUrl = $"{ChatConfig.NovuBackendUrl}/v1/messages/{messageId}";
					httpClient.DefaultRequestHeaders.Add("Authorization", $"ApiKey {ChatConfig.NovuSecretKey}");
					httpClient.DefaultRequestHeaders.Add("idempotency-key", Guid.NewGuid().ToString());

					var response = await httpClient.DeleteAsync(requestUrl);

					return response.IsSuccessStatusCode;
				}
			}
			catch (Exception e)
			{
				Logging.LogException(e, "Failed to delete novu message");
				return false;
			}
		}

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

		private async Task<bool> SendNotification(string title, string body, string recipientId, string eventCode,
			string type, bool enableCustomSounds, int count, string color, string workflowIdentifier, string sound)
		{
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
									},
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
								["gcm.message_id"] = "123"
							},
						},
						to = new[]{ new
					{
						subscriberId = recipientId
					}},
					};

					var payloadString = JsonConvert.SerializeObject(payload);
					var content = new StringContent(
						payloadString,
						Encoding.UTF8,
						"application/json");

					var result = await httpClient.PostAsync("v1/events/trigger", content);

					// A rejected trigger (unknown workflow identifier, unknown subscriber, bad payload) is a
					// 4xx with a body explaining why. Returning the bare bool made every one of those silent,
					// so a workflow that was never created in Novu looked exactly like a delivered push.
					if (!result.IsSuccessStatusCode)
					{
						var error = await result.Content.ReadAsStringAsync();
						Logging.LogError($"Novu trigger failed ({(int)result.StatusCode} {result.StatusCode}) workflow '{workflowIdentifier}' subscriber '{recipientId}' event '{eventCode}': {DescribeErrorBody(error)}");

						return false;
					}

					return true;
				}
			}
			catch (Exception e)
			{
				Logging.LogException(e, "Failed to send novu notification");
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
				type = type
			};

			var appleNotification = JsonConvert.SerializeObject(apnsPayload);

			return appleNotification;
		}

		#endregion Private Push Helpers
	}
}
