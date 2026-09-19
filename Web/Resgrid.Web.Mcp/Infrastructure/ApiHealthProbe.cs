using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Resgrid.Web.Mcp.Models;

namespace Resgrid.Web.Mcp.Infrastructure
{
	/// <summary>
	/// Reads the Resgrid API's anonymous v4 Health endpoint and lifts the Stripe Connect webhook block out of it
	/// (plan B2.5a). Kept static and dependency-free so the parsing is unit-testable and the MCP server stays a pure
	/// API client. Never throws: any failure yields <see cref="PaymentsWebhookHealthResult.Unavailable"/>.
	/// </summary>
	public static class ApiHealthProbe
	{
		/// <summary>Relative route of the API's v4 health read on the ResgridApi client's base address.</summary>
		public const string HealthPath = "api/v4/Health/GetCurrent";

		public static async Task<PaymentsWebhookHealthResult> ReadPaymentsAsync(HttpClient client)
		{
			try
			{
				if (client == null)
					return PaymentsWebhookHealthResult.Unavailable();

				using var response = await client.GetAsync(HealthPath);
				if (!response.IsSuccessStatusCode)
					return PaymentsWebhookHealthResult.Unavailable();

				var json = await response.Content.ReadAsStringAsync();
				return ParsePayments(json);
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "MCP health: the API's Payments health could not be read.");
				return PaymentsWebhookHealthResult.Unavailable();
			}
		}

		/// <summary>
		/// Parses a v4 HealthResult payload. An API that predates the Payments fields (no PaymentsWebhookHealthy) is
		/// reported as unavailable rather than healthy, so an old API is never mistaken for a healthy webhook.
		/// </summary>
		public static PaymentsWebhookHealthResult ParsePayments(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return PaymentsWebhookHealthResult.Unavailable();

			JObject root;
			try
			{
				root = JObject.Parse(json);
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex, "MCP health: the API's Payments health payload could not be parsed.");
				return PaymentsWebhookHealthResult.Unavailable();
			}

			var data = root["Data"] as JObject ?? root["data"] as JObject;
			if (data == null)
				return PaymentsWebhookHealthResult.Unavailable();

			var healthy = Bool(data, "PaymentsWebhookHealthy");
			if (healthy == null)
				return PaymentsWebhookHealthResult.Unavailable();

			return new PaymentsWebhookHealthResult
			{
				Available = true,
				StripeConnectEnabled = Bool(data, "PaymentsStripeConnectEnabled") ?? false,
				WebhookConfigured = Bool(data, "PaymentsWebhookConfigured") ?? false,
				WebhookEndpointRegistered = Bool(data, "PaymentsWebhookEndpointRegistered"),
				WebhookLastReceivedOn = Date(data, "PaymentsWebhookLastReceivedOn"),
				WebhookLastAppliedOn = Date(data, "PaymentsWebhookLastAppliedOn"),
				WebhookStale = Bool(data, "PaymentsWebhookStale") ?? false,
				WebhookRejectedLastHour = Int(data, "PaymentsWebhookRejectedLastHour"),
				WebhookFailedLastHour = Int(data, "PaymentsWebhookFailedLastHour"),
				OverdueOpenRequests = Int(data, "PaymentsOverdueOpenRequests"),
				LastReconcileOn = Date(data, "PaymentsLastReconcileOn"),
				WebhookHealthy = healthy
			};
		}

		private static JToken Field(JObject obj, string name)
		{
			var token = obj.GetValue(name, StringComparison.OrdinalIgnoreCase);
			return token == null || token.Type == JTokenType.Null ? null : token;
		}

		private static bool? Bool(JObject obj, string name)
		{
			var token = Field(obj, name);
			if (token == null)
				return null;
			if (token.Type == JTokenType.Boolean)
				return token.Value<bool>();
			return bool.TryParse(token.ToString(), out var parsed) ? parsed : (bool?)null;
		}

		private static int Int(JObject obj, string name)
		{
			var token = Field(obj, name);
			if (token == null)
				return 0;
			return int.TryParse(token.ToString(), out var parsed) ? parsed : 0;
		}

		private static DateTime? Date(JObject obj, string name)
		{
			var token = Field(obj, name);
			if (token == null)
				return null;
			if (token.Type == JTokenType.Date)
				return token.Value<DateTime>().ToUniversalTime();
			return DateTime.TryParse(token.ToString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : (DateTime?)null;
		}
	}
}
