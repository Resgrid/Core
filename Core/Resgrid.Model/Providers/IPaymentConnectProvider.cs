using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// One payment provider a department can connect its own account to (Workforce &amp; Business Operations plan,
	/// B2.3). v1 registers Stripe Connect only; Square, PayPal and Authorize.net are v2 adapters behind this same
	/// seam. Every method takes the department's external account id rather than a stored token, so a provider
	/// that issues per-merchant tokens decrypts them itself from the connection row it is handed.
	/// </summary>
	public interface IPaymentConnectProvider
	{
		/// <summary><see cref="PaymentProviders"/> value this adapter serves.</summary>
		int Provider { get; }

		/// <summary>True when the platform credentials for this adapter are configured in this process.</summary>
		bool IsConfigured { get; }

		/// <summary>The provider's authorize URL for the OAuth hand-off; <paramref name="state"/> is opaque and single use.</summary>
		string BuildConnectUrl(string state, string redirectUrl);

		/// <summary>Exchanges the callback (code) for the account facts. Throws InvalidOperationException("payments_*") on refusal.</summary>
		Task<PaymentConnectionFacts> CompleteConnectAsync(IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default);

		/// <summary>Re-reads the account (charges_enabled, capabilities, business name).</summary>
		Task<PaymentConnectionFacts> VerifyConnectionAsync(DepartmentPaymentConnection connection, CancellationToken cancellationToken = default);

		/// <summary>Revokes the platform's access to the account. Idempotent; an already-revoked account is not an error.</summary>
		Task DisconnectAsync(DepartmentPaymentConnection connection, CancellationToken cancellationToken = default);

		/// <summary>Opens a hosted payment page for one invoice balance as a direct charge on the connected account.</summary>
		Task<PaymentRequestCreation> CreatePaymentRequestAsync(DepartmentPaymentConnection connection, PaymentRequestSpec spec, CancellationToken cancellationToken = default);

		/// <summary>Reads a hosted request back (reconciliation), including the settled charge facts when paid.</summary>
		Task<PaymentRequestState> GetPaymentRequestStateAsync(DepartmentPaymentConnection connection, string externalReference, CancellationToken cancellationToken = default);

		/// <summary>Expires an open hosted request so its link stops working.</summary>
		Task CancelPaymentRequestAsync(DepartmentPaymentConnection connection, string externalReference, CancellationToken cancellationToken = default);

		/// <summary>Settled charge facts for a payment intent (the webhook body carries ids only).</summary>
		Task<PaymentChargeFacts> GetChargeFactsAsync(DepartmentPaymentConnection connection, string paymentIntentId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Verifies the webhook signature and parses the body into an envelope. False (with <paramref name="error"/>)
		/// when the signature is missing, stale or wrong; the body is never trusted before this returns true.
		/// </summary>
		bool TryParseWebhook(string rawBody, string signatureHeader, out PaymentProviderEventEnvelope envelope, out string error);
	}
}
