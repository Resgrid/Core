using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Providers;
using RestSharp;

namespace Resgrid.Providers.Marketing
{
	public class MailerliteEmailMarketing : IEmailMarketingProvider
	{
		public async Task<bool> Unsubscribe(string emailAddress)
		{
			//try
			//{
			//	var client = new RestClient(Config.MarketingConfig.MailerlteUrl);
			//	var request = new RestRequest("/api/v1/subscribers/unsubscribe/", Method.Post);
			//	request.AddObject(new
			//	{
			//		apiKey = Config.MarketingConfig.MailingApiKey,
			//		email = emailAddress
			//	});
			//	var response = await client.ExecuteAsync(request);

			//	return true;
			//}
			//catch { }

			return false;
		}

		public async Task<bool> SubscribeUserToAdminList(string firstName, string lastName, string emailAddress)
		{
			//try
			//{
			//	var client = new RestClient(Config.MarketingConfig.MailerlteUrl);
			//	var request = new RestRequest(string.Format("api/v1/subscribers/{0}/", Config.MarketingConfig.AdminListId), Method.Post);
			//	request.AddObject(new
			//	{
			//		apiKey = Config.MarketingConfig.MailingApiKey,
			//		email = emailAddress,
			//		name = firstName,
			//		fields = new List<dynamic>
			//		{
			//			new
			//			{
			//				name = "last_name",
			//				value = lastName
			//			}
			//		}
			//	});
			//	var response = await client.ExecuteAsync(request);

			//	return true;
			//}
			//catch { }

			return false;
		}

		public async Task<bool> SubscribeUserToUsersList(string firstName, string lastName, string emailAddress)
		{
			try
			{
				var client = new RestClient(Config.MarketingConfig.MailerlteUrl);
				var request = new RestRequest(string.Format("api/v1/subscribers/{0}/", Config.MarketingConfig.UserListId), Method.Post);
				request.AddObject(new
				{
					apiKey = Config.MarketingConfig.MailingApiKey,
					email = emailAddress,
					name = firstName,
					fields = new List<dynamic>
					{
						new
						{
							name = "last_name",
							value = lastName
						}
					}
				});
				var response = await client.ExecuteAsync(request);

				return true;
			}
			catch { }

			return false;
		}

		public async Task<bool> IncreaseStatusPageMetric(string metric)
		{
			try
			{
				var client = new RestClient(Config.StatusSystemConfig.StatusPageBaseUrl);
				var setMetricRequest = new RestRequest($"api/v1/metrics/{metric}/points", Method.Post);
				setMetricRequest.AddHeader("X-Cachet-Token", Config.StatusSystemConfig.ApiToken);
				setMetricRequest.AddParameter("application/json", "{\"value\":\"1\"}", ParameterType.RequestBody);

				var response = await client.ExecuteAsync(setMetricRequest);

				return true;
			}
			catch { }

			return false;
		}
	}
}
