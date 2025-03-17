using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Novu;
using Novu.Domain.Models.Notifications;
using Novu.Domain.Models.Subscribers;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Providers;
using Resgrid.Providers.Messaging.Novu.Model;
using Sentry.Protocol;
using static Resgrid.Framework.Testing.TestData;

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

					if (response.IsSuccessStatusCode)
						return true;
					else
						return false;
				}
			}
			catch (Exception e)
			{
				Logging.LogException(e, "Failed to create novu subscriber");
				return false;
			}
		}

		public async Task<bool> CreateUserSubscriber(string userId, string code, int departmentId, string email, string firstName, string lastName)
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

					if (response.IsSuccessStatusCode)
						return true;
					else
						return false;
				}
			}
			catch (Exception e)
			{
				Logging.LogException(e, "Failed to add fcm token to novu subscriber");
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
	}
}
