using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services.Invoicing
{
	/// <summary>
	/// Phase B2 online payments. Scaffolded 2026-09-18 with the webhook health read only (plan B2.4 "Health" and
	/// B2.5a); connect, pay-link, webhook-apply and reconciliation land with M0212.
	/// </summary>
	public class InvoicePaymentsService : IInvoicePaymentsService
	{
		/// <summary>Connect events the webhook receiver consumes (plan B2.1). The endpoint probe requires all of them.</summary>
		public static readonly IReadOnlyCollection<string> StripeConnectRequiredEvents = new[]
		{
			"checkout.session.completed",
			"checkout.session.async_payment_succeeded",
			"checkout.session.async_payment_failed",
			"checkout.session.expired",
			"payment_intent.succeeded",
			"charge.refunded",
			"charge.dispute.created",
			"charge.dispute.closed",
			"account.application.deauthorized",
			"account.updated"
		};

		private static readonly TimeSpan EndpointProbeCacheWindow = TimeSpan.FromMinutes(15);
		private static readonly object EndpointProbeLock = new object();
		private static DateTime _endpointProbeCheckedOnUtc = DateTime.MinValue;
		private static bool? _endpointProbeResult;
		private static string _endpointProbeUrl;

		private readonly IFeatureToggleService _featureToggleService;
		private readonly IStripeConnectEndpointProbe _endpointProbe;

		public InvoicePaymentsService(IFeatureToggleService featureToggleService, IStripeConnectEndpointProbe endpointProbe)
		{
			_featureToggleService = featureToggleService;
			_endpointProbe = endpointProbe;
		}

		public async Task<PaymentsWebhookHealth> GetWebhookHealthAsync()
		{
			var health = new PaymentsWebhookHealth();

			try
			{
				health.Enabled = Config.PaymentConnectConfig.Enabled && await IsClusterSwitchOnAsync();
				if (!health.Enabled)
				{
					health.ComputeHealthy();
					return health;
				}

				health.WebhookConfigured = !string.IsNullOrWhiteSpace(Config.PaymentConnectConfig.StripeSecretKey)
					&& !string.IsNullOrWhiteSpace(Config.PaymentConnectConfig.StripeConnectWebhookSecret)
					&& !string.IsNullOrWhiteSpace(Config.PaymentConnectConfig.GetWebhookUrl());

				if (health.WebhookConfigured && Config.PaymentConnectConfig.WebhookEndpointProbeEnabled)
					health.EndpointRegistered = await ProbeEndpointRegisteredCachedAsync();

				// LastEventReceivedOn, LastEventAppliedOn, Stale, RejectedLastHour, FailedLastHour, OverdueOpenRequests and
				// LastReconcileOn read PaymentProviderEvents and InvoicePaymentRequests, which arrive with M0212 (plan B2.2).
				// Until then they hold their defaults: no activity, nothing stale, nothing rejected.

				health.ComputeHealthy();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Payments webhook health could not be read.");
				health.EndpointRegistered = null;
				health.ComputeHealthy();
			}

			return health;
		}

		/// <summary>
		/// The Payments.StripeConnect operator flag is a per-cluster switch: only its global state counts, never a
		/// department override, so it is read as a flag row rather than evaluated for a department.
		/// </summary>
		private async Task<bool> IsClusterSwitchOnAsync()
		{
			var flag = await _featureToggleService.GetFlagByKeyAsync(FeatureFlagKeys.PaymentsStripeConnect);
			return flag != null && flag.IsEnabledGlobally && !flag.IsArchived;
		}

		private async Task<bool?> ProbeEndpointRegisteredCachedAsync()
		{
			var url = Config.PaymentConnectConfig.GetWebhookUrl();
			var now = DateTime.UtcNow;

			lock (EndpointProbeLock)
			{
				if (_endpointProbeUrl == url && now - _endpointProbeCheckedOnUtc < EndpointProbeCacheWindow)
					return _endpointProbeResult;
			}

			bool? result;
			try
			{
				result = await _endpointProbe.IsEndpointRegisteredAsync(url, Config.PaymentConnectConfig.StripeLiveMode, StripeConnectRequiredEvents);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Stripe Connect webhook endpoint probe failed.");
				result = null;
			}

			lock (EndpointProbeLock)
			{
				_endpointProbeUrl = url;
				_endpointProbeCheckedOnUtc = now;
				_endpointProbeResult = result;
			}

			return result;
		}

		/// <summary>Clears the in-process endpoint probe cache (tests, and an operator action after re-registering the endpoint).</summary>
		public static void ResetEndpointProbeCache()
		{
			lock (EndpointProbeLock)
			{
				_endpointProbeUrl = null;
				_endpointProbeCheckedOnUtc = DateTime.MinValue;
				_endpointProbeResult = null;
			}
		}
	}
}
