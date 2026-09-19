using System;

namespace Resgrid.Web.Mcp.Models
{
	/// <summary>
	/// Stripe Connect webhook health as reported by the Resgrid API's v4 Health endpoint (Workforce &amp; Business
	/// Operations plan, B2.5a), mirrored here so one MCP health read shows the whole chain. The MCP server is an API
	/// client: it never holds the Stripe platform key and never reads the payment tables, it relays what the API says.
	/// Value-free by construction: no URL, secret, account, department or invoice identifier.
	/// </summary>
	public sealed class PaymentsWebhookHealthResult
	{
		/// <summary>The API answered and the block below reflects its current state. False when the API could not be reached or its payload had no Payments fields (an older API).</summary>
		public bool Available { get; set; }

		/// <summary>Department-connected Stripe payment collection is enabled in the API process (config and the Payments.StripeConnect operator flag). False in a cluster where it is not offered.</summary>
		public bool StripeConnectEnabled { get; set; }

		/// <summary>The API has the platform key, the Connect webhook secret and its public base URL.</summary>
		public bool WebhookConfigured { get; set; }

		/// <summary>Stripe lists an enabled webhook endpoint at the API's webhook URL with the required events. Null when the API's probe was skipped or Stripe was unreachable.</summary>
		public bool? WebhookEndpointRegistered { get; set; }

		/// <summary>UTC time the newest Connect webhook event was received by the API, if any.</summary>
		public DateTime? WebhookLastReceivedOn { get; set; }

		/// <summary>UTC time the newest Connect webhook event was applied to an invoice, if any.</summary>
		public DateTime? WebhookLastAppliedOn { get; set; }

		/// <summary>Payment activity recently but no webhook event within the API's staleness window.</summary>
		public bool WebhookStale { get; set; }

		/// <summary>Webhook events the API rejected in the last hour.</summary>
		public int WebhookRejectedLastHour { get; set; }

		/// <summary>Webhook events the API failed to apply in the last hour.</summary>
		public int WebhookFailedLastHour { get; set; }

		/// <summary>Open payment requests the API has not resolved in time.</summary>
		public int OverdueOpenRequests { get; set; }

		/// <summary>UTC time of the API's last successful reconciliation pass, if any.</summary>
		public DateTime? LastReconcileOn { get; set; }

		/// <summary>The API's one-line verdict. Null when <see cref="Available"/> is false, so a monitor can tell "unknown" from "unhealthy".</summary>
		public bool? WebhookHealthy { get; set; }

		/// <summary>The value reported when the API could not be read.</summary>
		public static PaymentsWebhookHealthResult Unavailable()
		{
			return new PaymentsWebhookHealthResult { Available = false, WebhookHealthy = null };
		}
	}
}
