using System.Text;
using Newtonsoft.Json;
using Novu;
using Novu.Domain.Models.Subscribers;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Providers;
using static Resgrid.Framework.Testing.TestData;

namespace Resgrid.Providers.Messaging
{
	public class NovuProvider : INovuProvider
	{
		private async Task<bool> CreateSubscriber(string id, int departmentId, string email, string firstName, string lastName)
		{
			try
			{
				var novuConfiguration = new NovuClientConfiguration
				{
					Url = $"{Config.ChatConfig.NovuBackendUrl}/v1", //"https://novu-api.my-domain.com/v1",
					ApiKey = Config.ChatConfig.NovuSecretKey //"12345",
				};

				var novu = new NovuClient(novuConfiguration);

				var subscriberCreateData = new SubscriberCreateData();
				subscriberCreateData.SubscriberId = id;
				subscriberCreateData.FirstName = firstName;
				subscriberCreateData.LastName = lastName;
				subscriberCreateData.Email = email;
				//subscriberCreateData.Data = new List<AdditionalData>();
				//subscriberCreateData.Data.Add(new AdditionalData
				//{
				//	Key = "DepartmentId",
				//	Value = departmentId.ToString()
				//});

				var subscriber = await novu.Subscriber.Create(subscriberCreateData);

				if (subscriber != null && subscriber.Data != null)
					return true;

				return false;
			}
			catch (Exception e)
			{
				Logging.LogException(e, "Failed to create novu subscriber");
				return false;
			}
		}

		public async Task<bool> CreateUserSubscriber(string userId, string code, int departmentId, string email, string firstName, string lastName)
		{
			return await CreateSubscriber($"{code}_User_{userId}", departmentId, email, firstName, lastName);
		}

		public async Task<bool> CreateUnitSubscriber(int unitId, string code, int departmentId, string unitName)
		{
			return await CreateSubscriber($"{code}_Unit_{unitId}", departmentId, $"{code}_Unit_{unitId}@units.resgrid.net", unitName, "");
		}

		private async Task<bool> UpdateSubscriberFcm(string id, string token, string fcmId)
		{
			try
			{
				using (HttpClient client = new HttpClient())
				{
					var url = $"${ChatConfig.NovuBackendUrl}v1/subscribers/{id}/credentials";
					var request = new HttpRequestMessage(HttpMethod.Put, url);
					request.Headers.Add("Accept", "application/json");
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
