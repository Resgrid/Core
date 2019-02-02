using System.Collections.Generic;
using Resgrid.Model.Providers;
using RestSharp;

namespace Resgrid.Providers.Marketing
{
	public class MailerliteEmailMarketing : IEmailMarketingProvider
	{
		public void Unsubscribe(string emailAddress)
		{
			try
			{
				var client = new RestClient(Config.MarketingConfig.MailerlteUrl);
				var request = new RestRequest("/api/v1/subscribers/unsubscribe/", Method.POST);
				request.AddObject(new
				{
					apiKey = Config.MarketingConfig.MailingApiKey,
					email = emailAddress
				});
				var response = client.Execute(request);
			}
			catch { }
		}

		public void SubscribeUserToAdminList(string firstName, string lastName, string emailAddress)
		{
			try
			{
				var client = new RestClient(Config.MarketingConfig.MailerlteUrl);
				var request = new RestRequest(string.Format("api/v1/subscribers/{0}/", Config.MarketingConfig.AdminListId), Method.POST);
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
				var response = client.Execute(request);
			}
			catch { }
		}

		public void SubscribeUserToUsersList(string firstName, string lastName, string emailAddress)
		{
			try
			{
				var client = new RestClient(Config.MarketingConfig.MailerlteUrl);
				var request = new RestRequest(string.Format("api/v1/subscribers/{0}/", Config.MarketingConfig.UserListId), Method.POST);
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
				var response = client.Execute(request);
			}
			catch { }
		}

		public void IncreaseStatusPageMetric(string metric)
		{
			try
			{
				var client = new RestClient(Config.StatusSystemConfig.StatusPageBaseUrl);
				var setMetricRequest = new RestRequest($"api/v1/metrics/{metric}/points", Method.POST);
				setMetricRequest.AddHeader("X-Cachet-Token", Config.StatusSystemConfig.ApiToken);
				setMetricRequest.AddParameter("application/json", "{\"value\":\"1\"}", ParameterType.RequestBody);

				var response = client.Execute(setMetricRequest);
			}
			catch { }
		}
	}
}
