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
		/// <summary>
		/// A health probe must answer quickly or not at all: Stripe.net's default transport waits 80 seconds and retries
		/// twice, which would hold the anonymous v4 Health call whenever Stripe is slow. One shared transport, short
		/// timeout, no retries; a timeout surfaces as an exception the caller records as an unknown result.
		/// </summary>
		private static readonly SystemNetHttpClient Transport = new SystemNetHttpClient(
			new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(ProbeTimeoutSeconds) }, maxNetworkRetries: 0);

		private const int ProbeTimeoutSeconds = 5;
		private const int EndpointListLimit = 100;

		public async Task<bool?> IsEndpointRegisteredAsync(string expectedUrl, bool liveMode, IReadOnlyCollection<string> requiredEvents)
		{
			if (string.IsNullOrWhiteSpace(Config.PaymentConnectConfig.StripeSecretKey) || string.IsNullOrWhiteSpace(expectedUrl))
				return null;

			var client = new StripeClient(Config.PaymentConnectConfig.StripeSecretKey, httpClient: Transport);
			var service = new WebhookEndpointService(client);
			var endpoints = await service.ListAsync(new WebhookEndpointListOptions { Limit = EndpointListLimit });

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
