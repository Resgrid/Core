using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Novu.Domain.Models.Subscribers;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus.Models;


namespace Resgrid.Providers.Messaging
{
	public class NovuProvider : INovuProvider
	{
		private async Task<bool> CreateSubscriber(string id, int departmentId, string email, string firstName, string lastName, List<AdditionalData> data)
		{
			try
			{
				using (var httpClient = new HttpClient())
				{
					var requestUrl = $"{ChatConfig.NovuBackendUrl}/v2/subscribers";
					httpClient.DefaultRequestHeaders.Add("idempotency-key", Guid.NewGuid().ToString());
					httpClient.DefaultRequestHeaders.Add("Authorization", Config.ChatConfig.NovuSecretKey);

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
					string jsonContent = JsonConvert.SerializeObject(payload);

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
			return await CreateSubscriber($"{code}_User_{userId}", departmentId, email, firstName, lastName, null);
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
						providerId = "fcm",
						credentials = new
						{
							deviceTokens = new string[] { token }
						},
						integrationIdentifier = fcmId
					};
					string jsonContent = JsonConvert.SerializeObject(payload);

					request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
					HttpResponseMessage response = await client.SendAsync(request);

					return response.IsSuccessStatusCode;
				}
			}
			catch (Exception e)
			{
				Logging.LogException(e, "Failed to add fcm token to novu subscriber");
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

		public async Task<bool> UpdateUnitSubscriberFcm(int unitId, string code, string token)
		{
			return await UpdateSubscriberFcm($"{code}_Unit_{unitId}", token, ChatConfig.NovuUnitFcmProviderId);
		}

		private async Task<bool> SendNotification(string title, string body, string recipientId, string eventCode,
			string type, bool enableCustomSounds, int count, string color, string workflowIdentifier)
		{
			try
			{
				using (var httpClient = new HttpClient())
				{
					// Set base URL and headers
					httpClient.BaseAddress = new Uri(ChatConfig.NovuBackendUrl);
					httpClient.DefaultRequestHeaders.Add("Authorization", $"ApiKey {ChatConfig.NovuSecretKey}");
					httpClient.DefaultRequestHeaders.Add("idempotency-key", Guid.NewGuid().ToString());

					string androidChannelName = GetAndroidChannelName(eventCode);
					// Build request payload
					var payload = new
					{
						name = workflowIdentifier,
						payload = new
						{
							subject = title,
							body = body,
							//inAppAvatar
							//arrowImage

						},
						overrides = new
						{
							fcm = new
							{
								android = new
								{
									priority = androidChannelName == "calls" ? "high" : "normal",
									notification = new
									{
										channelId = type,
										defaultSound = true,
										sticky = androidChannelName == "calls" ? true : false,
										//priority = androidChannelName == "calls" ? 5 : 3,
										priority = androidChannelName == "calls" ? "max" : "default",
									},
									data = new
									{
										title = title,
										message = body,
										eventCode = eventCode,
										type = type
									}
								}//,
								//data = new
								//{
								//	title = title,
								//	message = body,
								//	eventCode = eventCode,
								//	type = type
								//}
							}
						},
						to = new[]{ new
					{
						subscriberId = recipientId
					}},
					};

					var content = new StringContent(
						JsonConvert.SerializeObject(payload),
						Encoding.UTF8,
						"application/json");

					var result = await httpClient.PostAsync("v1/events/trigger", content);

					return result.IsSuccessStatusCode;
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
			return await SendNotification(title, body, $"{depCode}_Unit_{unitId}", eventCode, type, enableCustomSounds, count, color, ChatConfig.NovuDispatchUnitWorkflowId);
		}

		#region Private Push Helpers

		private string GetSoundFileNameFromType(Platforms platform, string type)
		{
			if (type == ((int)PushSoundTypes.CallEmergency).ToString())
			{
				if (platform == Platforms.iPhone)
					return "callemergency.caf";

				return "callemergency.wav";
			}
			else if (type == ((int)PushSoundTypes.CallHigh).ToString())

			{
				if (platform == Platforms.iPhone)
					return "callhigh.caf";

				return "callhigh.mp3";
			}
			else if (type == ((int)PushSoundTypes.CallMedium).ToString())
			{
				if (platform == Platforms.iPhone)
					return "callmedium.caf";

				return "callmedium.mp3";
			}
			else if (type == ((int)PushSoundTypes.CallLow).ToString())
			{
				if (platform == Platforms.iPhone)
					return "calllow.caf";

				return "calllow.mp3";
			}
			else if (type == ((int)PushSoundTypes.Notifiation).ToString())
			{
				if (platform == Platforms.iPhone)
					return "notification.caf";

				return "notification.mp3";
			}
			else if (type == ((int)PushSoundTypes.Message).ToString())
			{
				if (platform == Platforms.iPhone)
					return "message.caf";

				return "message.mp3";
			}
			else
			{
				if (platform == Platforms.iPhone)
					return $"{type}.caf";

				return $"{type}.mp3";
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

			string soundFilename = FormatForAndroidNativePush(GetSoundFileNameFromType(Platforms.Android, type));

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
						name = GetSoundFileNameFromType(Platforms.iPhone, type),
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
