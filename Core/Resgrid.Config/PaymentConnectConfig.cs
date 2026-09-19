namespace Resgrid.Config
{
	/// <summary>
	/// Department-connected Stripe accounts for collecting payments on Resgrid invoices (Workforce &amp; Business
	/// Operations plan, Phase B2). Off unless this switch is on; the operator feature flag Payments.StripeConnect is
	/// the second, per-cluster lock, so a cluster that does not offer payment collection leaves both off. These are
	/// the platform's own Connect credentials and are deliberately separate from PaymentProviderConfig, which serves
	/// Resgrid's SaaS subscription billing even when both point at the same Stripe account. Environment keys:
	/// RESGRID:PaymentConnectConfig:Enabled, :CredentialPassphrase, :PublicBaseUrl, :StripeClientId, :StripeSecretKey,
	/// :StripeConnectWebhookSecret, :StripeLiveMode, :PayLinkTokenTtlDays, :RequestReconcileAfterMinutes,
	/// :EventRetentionDays, :PayPageRateLimitPerMinute, :WebhookStaleAfterHours, :WebhookEndpointProbeEnabled.
	/// </summary>
	public static class PaymentConnectConfig
	{
		/// <summary>Process-level master switch. Off means no provider is registered, no connection can be made and the webhook endpoint answers 503.</summary>
		public static bool Enabled = false;

		/// <summary>
		/// Passphrase for the symmetric encryption of stored provider tokens. Stripe Connect stores no per-account token,
		/// so it is unused in v1, but a later token-holding provider (Square, PayPal, Authorize.net) cannot be enabled without it.
		/// </summary>
		public static string CredentialPassphrase = "";

		/// <summary>Public origin of this cluster (for example https://api.resgrid.com). The pay page, the OAuth redirect and the webhook URL are built from this and nothing else.</summary>
		public static string PublicBaseUrl = "";

		/// <summary>Connect OAuth client id of the platform (ca_...). Live and sandbox ids differ.</summary>
		public static string StripeClientId = "";

		/// <summary>The platform's secret key used for the OAuth token exchange and for every call made as a connected account.</summary>
		public static string StripeSecretKey = "";

		/// <summary>Signing secret of the Connect-scoped webhook endpoint (events from connected accounts). Never the SaaS endpoint's secret.</summary>
		public static string StripeConnectWebhookSecret = "";

		/// <summary>Whether the keys above are live-mode keys. A webhook whose livemode does not match is rejected.</summary>
		public static bool StripeLiveMode = false;

		/// <summary>How long an e-mailed or printed pay-page link stays valid.</summary>
		public static int PayLinkTokenTtlDays = 30;

		/// <summary>Age after which an open payment request is polled at Stripe by the invoice maintenance worker instead of waiting for a webhook.</summary>
		public static int RequestReconcileAfterMinutes = 10;

		/// <summary>Days raw webhook bodies are kept for forensics before the worker purges them.</summary>
		public static int EventRetentionDays = 90;

		/// <summary>Per-IP request limit on the anonymous pay page.</summary>
		public static int PayPageRateLimitPerMinute = 20;

		/// <summary>Hours without any webhook event, while there was payment activity in the last seven days, before the health check reports the webhook as stale.</summary>
		public static int WebhookStaleAfterHours = 24;

		/// <summary>Whether the health check may ask Stripe whether an enabled webhook endpoint exists at this cluster's webhook URL (one call per process per fifteen minutes).</summary>
		public static bool WebhookEndpointProbeEnabled = true;

		/// <summary>Route of the Connect webhook receiver on the Web.Services host. A constant, so the config processor never overwrites it.</summary>
		public const string WebhookPath = "/api/PaymentWebhooks/stripe";

		/// <summary>The webhook URL Stripe must be configured with for this cluster, or an empty string when PublicBaseUrl is not set.</summary>
		public static string GetWebhookUrl()
		{
			if (string.IsNullOrWhiteSpace(PublicBaseUrl))
				return string.Empty;

			return PublicBaseUrl.TrimEnd('/') + WebhookPath;
		}
	}
}
