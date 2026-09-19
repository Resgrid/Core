using System;

namespace Resgrid.Model.Invoicing
{
	/// <summary>
	/// Value-free health of the Stripe Connect webhook path (Workforce &amp; Business Operations plan, B2.5a). It is
	/// returned on the anonymous v4 Health endpoint, so it carries no URL, secret, account, department or invoice
	/// identifier: booleans, counts and timestamps only. A cluster where payment collection is switched off reports
	/// <see cref="Enabled"/> false and <see cref="Healthy"/> true, because an intentionally disabled subsystem is not unhealthy.
	/// </summary>
	public class PaymentsWebhookHealth
	{
		/// <summary>PaymentConnectConfig.Enabled and the Payments.StripeConnect operator flag both hold in this process.</summary>
		public bool Enabled { get; set; }

		/// <summary>The platform secret key, the Connect webhook secret and the public base URL are all present.</summary>
		public bool WebhookConfigured { get; set; }

		/// <summary>
		/// Stripe lists an enabled webhook endpoint at this cluster's webhook URL, in the configured live/test mode, whose
		/// enabled events cover everything the receiver consumes. Null when the probe is disabled, skipped or Stripe could
		/// not be reached. The API does not expose whether an endpoint is Connect-scoped; that is confirmed once at registration.
		/// </summary>
		public bool? EndpointRegistered { get; set; }

		/// <summary>UTC time the newest webhook event was received, if any.</summary>
		public DateTime? LastEventReceivedOn { get; set; }

		/// <summary>UTC time the newest webhook event was applied to an invoice, if any.</summary>
		public DateTime? LastEventAppliedOn { get; set; }

		/// <summary>There was payment activity in the last seven days but no event within PaymentConnectConfig.WebhookStaleAfterHours.</summary>
		public bool Stale { get; set; }

		/// <summary>Events rejected in the last hour (bad signature, live-mode mismatch).</summary>
		public int RejectedLastHour { get; set; }

		/// <summary>Events that failed to apply in the last hour.</summary>
		public int FailedLastHour { get; set; }

		/// <summary>Open payment requests older than PaymentConnectConfig.RequestReconcileAfterMinutes that neither a webhook nor the worker has resolved.</summary>
		public int OverdueOpenRequests { get; set; }

		/// <summary>UTC time of the invoice maintenance worker's last successful reconciliation pass, if any.</summary>
		public DateTime? LastReconcileOn { get; set; }

		/// <summary>The one-line verdict uptime monitoring keys on. See <see cref="ComputeHealthy"/>.</summary>
		public bool Healthy { get; set; } = true;

		/// <summary>The value a cluster reports when payment collection is off.</summary>
		public static PaymentsWebhookHealth Disabled()
		{
			return new PaymentsWebhookHealth { Enabled = false, Healthy = true };
		}

		/// <summary>
		/// Disabled is healthy. Enabled is healthy when the webhook is configured, Stripe did not say the endpoint is
		/// missing (an unknown probe result does not fail the check), nothing is stale, nothing was rejected in the last
		/// hour and no open request is overdue.
		/// </summary>
		public void ComputeHealthy()
		{
			if (!Enabled)
			{
				Healthy = true;
				return;
			}

			Healthy = WebhookConfigured
				&& EndpointRegistered != false
				&& !Stale
				&& RejectedLastHour == 0
				&& OverdueOpenRequests == 0;
		}
	}
}
