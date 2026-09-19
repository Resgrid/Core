using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Payments
{
	/// <summary>The adapter served while PaymentConnectConfig.Enabled is false: every call refuses with payments_disabled and no webhook ever verifies.</summary>
	public sealed class NullPaymentConnectProvider : IPaymentConnectProvider
	{
		public const string DisabledCode = "payments_disabled";

		public int Provider => (int)PaymentProviders.Stripe;
		public bool IsConfigured => false;

		public string BuildConnectUrl(string state, string redirectUrl) => throw new InvalidOperationException(DisabledCode);
		public Task<PaymentConnectionFacts> CompleteConnectAsync(IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default) => throw new InvalidOperationException(DisabledCode);
		public Task<PaymentConnectionFacts> VerifyConnectionAsync(DepartmentPaymentConnection connection, CancellationToken cancellationToken = default) => throw new InvalidOperationException(DisabledCode);
		public Task DisconnectAsync(DepartmentPaymentConnection connection, CancellationToken cancellationToken = default) => throw new InvalidOperationException(DisabledCode);
		public Task<PaymentRequestCreation> CreatePaymentRequestAsync(DepartmentPaymentConnection connection, PaymentRequestSpec spec, CancellationToken cancellationToken = default) => throw new InvalidOperationException(DisabledCode);
		public Task<PaymentRequestState> GetPaymentRequestStateAsync(DepartmentPaymentConnection connection, string externalReference, CancellationToken cancellationToken = default) => throw new InvalidOperationException(DisabledCode);
		public Task CancelPaymentRequestAsync(DepartmentPaymentConnection connection, string externalReference, CancellationToken cancellationToken = default) => throw new InvalidOperationException(DisabledCode);
		public Task<PaymentChargeFacts> GetChargeFactsAsync(DepartmentPaymentConnection connection, string paymentIntentId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(DisabledCode);

		public bool TryParseWebhook(string rawBody, string signatureHeader, out PaymentProviderEventEnvelope envelope, out string error)
		{
			envelope = null;
			error = DisabledCode;
			return false;
		}
	}
}
