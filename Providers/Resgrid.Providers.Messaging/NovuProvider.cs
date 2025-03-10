using Novu;
using Novu.Domain.Models.Subscribers;
using Resgrid.Framework;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Messaging
{
	public class NovuProvider: INovuProvider
	{
		public async Task<bool> CreateSubscriber(string userId, int departmentId, string email, string firstName, string lastName)
		{
			try
			{
				var novuConfiguration = new NovuClientConfiguration
				{
					Url = Config.ChatConfig.NovuBackendUrl, //"https://novu-api.my-domain.com/v1",
					ApiKey = Config.ChatConfig.NovuSecretKey //"12345",
				};

				var novu = new NovuClient(novuConfiguration);

				var subscriberCreateData = new SubscriberCreateData();
				subscriberCreateData.SubscriberId = userId;
				subscriberCreateData.FirstName = firstName;
				subscriberCreateData.LastName = lastName;
				subscriberCreateData.Email = email;
				subscriberCreateData.Data = new List<AdditionalData>();
				subscriberCreateData.Data.Add(new AdditionalData
				{
					Key = "DepartmentId",
					Value = departmentId.ToString()
				});

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


	}
}
