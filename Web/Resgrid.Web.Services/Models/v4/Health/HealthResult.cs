using System;

namespace Resgrid.Web.Services.Models.v4.Health
{
	/// <summary>
	/// Result of the Health check API
	/// </summary>
	public class HealthResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public HealthResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public HealthResult()
		{
			Data = new HealthResultData();
		}
	}

	/// <summary>
	/// Health check data for the current state of the api server handling the request
	/// </summary>
	public class HealthResultData
	{
		/// <summary>
		/// Site\Location of this API
		/// </summary>
		public string SiteId { get; set; }

		/// <summary>
		/// The Version of the Services
		/// </summary>
		public string ServicesVersion { get; set; }

		/// <summary>
		/// Gets the current API version
		/// </summary>
		public string ApiVersion { get; set; }

		/// <summary>
		/// Can the API services talk to the database
		/// </summary>
		public bool DatabaseOnline { get; set; }

		/// <summary>
		/// Can the API services talk to the cache
		/// </summary>
		public bool CacheOnline { get; set; }

		/// <summary>Search host enabled in this process (SearchConfig.Enabled).</summary>
		public bool SearchEnabled { get; set; }

		/// <summary>A local reader is open for the global index (after a pull or a write).</summary>
		public bool SearchOnline { get; set; }

		/// <summary>Suppressed: shared-index counts disclose other departments' data volumes.</summary>
		public int? SearchIndexDocCount { get; set; }

		/// <summary>
		/// Department-connected Stripe payment collection is enabled in this process (PaymentConnectConfig.Enabled and
		/// the Payments.StripeConnect operator flag). False in a cluster where it is not offered; the other Payments
		/// fields then hold their defaults and PaymentsWebhookHealthy is true.
		/// </summary>
		public bool PaymentsStripeConnectEnabled { get; set; }

		/// <summary>The platform key, the Connect webhook secret and the public base URL are all present. Booleans only; the URL and secret are never returned.</summary>
		public bool PaymentsWebhookConfigured { get; set; }

		/// <summary>
		/// Stripe lists an enabled webhook endpoint at this cluster's webhook URL, in the configured live/test mode, whose
		/// enabled events cover everything the receiver consumes. Null when the probe is disabled, skipped or Stripe could not be reached.
		/// </summary>
		public bool? PaymentsWebhookEndpointRegistered { get; set; }

		/// <summary>UTC time the newest Connect webhook event was received, if any.</summary>
		public DateTime? PaymentsWebhookLastReceivedOn { get; set; }

		/// <summary>UTC time the newest Connect webhook event was applied to an invoice, if any.</summary>
		public DateTime? PaymentsWebhookLastAppliedOn { get; set; }

		/// <summary>Payment activity in the last seven days but no webhook event within PaymentConnectConfig.WebhookStaleAfterHours.</summary>
		public bool PaymentsWebhookStale { get; set; }

		/// <summary>Webhook events rejected in the last hour (bad signature, live-mode mismatch).</summary>
		public int PaymentsWebhookRejectedLastHour { get; set; }

		/// <summary>Webhook events that failed to apply in the last hour.</summary>
		public int PaymentsWebhookFailedLastHour { get; set; }

		/// <summary>Open payment requests that neither a webhook nor the invoice maintenance worker has resolved in time.</summary>
		public int PaymentsOverdueOpenRequests { get; set; }

		/// <summary>UTC time of the invoice maintenance worker's last successful reconciliation pass, if any.</summary>
		public DateTime? PaymentsLastReconcileOn { get; set; }

		/// <summary>
		/// The one-line verdict for uptime monitoring: disabled is healthy; enabled is healthy when configured, the
		/// endpoint is not known to be missing, nothing is stale, nothing was rejected in the last hour and no request is overdue.
		/// </summary>
		public bool PaymentsWebhookHealthy { get; set; } = true;
	}
}
