using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Providers;
using Stripe;

namespace Resgrid.Services.Invoicing
{
	/// <summary>
	/// Lists the platform account's webhook endpoints with the Connect platform key (plan B2.5a). Uses its own
	/// StripeClient so the SaaS billing path's global Stripe configuration is never touched.
	/// </summary>
	public sealed class StripeConnectEndpointProbe : IStripeConnectEndpointProbe
	{
		public async Task<bool?> IsEndpointRegisteredAsync(string expectedUrl, bool liveMode, IReadOnlyCollection<string> requiredEvents)
		{
			if (string.IsNullOrWhiteSpace(Config.PaymentConnectConfig.StripeSecretKey) || string.IsNullOrWhiteSpace(expectedUrl))
				return null;

			var client = new StripeClient(Config.PaymentConnectConfig.StripeSecretKey);
			var service = new WebhookEndpointService(client);
			var endpoints = await service.ListAsync(new WebhookEndpointListOptions { Limit = 100 });

			var expected = Normalize(expectedUrl);
			foreach (var endpoint in endpoints)
			{
				if (endpoint == null || endpoint.Deleted == true)
					continue;
				if (!string.Equals(Normalize(endpoint.Url), expected, StringComparison.OrdinalIgnoreCase))
					continue;
				if (!string.Equals(endpoint.Status, "enabled", StringComparison.OrdinalIgnoreCase))
					continue;
				if (endpoint.Livemode != liveMode)
					continue;

				var events = endpoint.EnabledEvents ?? new List<string>();
				if (events.Contains("*") || requiredEvents.All(e => events.Contains(e, StringComparer.OrdinalIgnoreCase)))
					return true;
			}

			return false;
		}

		private static string Normalize(string url)
		{
			return (url ?? string.Empty).Trim().TrimEnd('/');
		}
	}
}
