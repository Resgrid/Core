using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Online payment collection on Resgrid invoices through a department's own provider account (Workforce &amp;
	/// Business Operations plan, Phase B2, B2.4). Resgrid never holds funds: every request is a direct charge on the
	/// connected account, and the verified webhook is the only thing that records money. Refusals throw
	/// InvalidOperationException("payments_*") with a resource-key code, the invoicing convention.
	/// </summary>
	public interface IInvoicePaymentsService
	{
		/// <summary>PaymentConnectConfig.Enabled and the Payments.StripeConnect operator flag both hold in this cluster (global state only).</summary>
		Task<bool> IsAvailableInClusterAsync();

		/// <summary>The department's posture: cluster switch, department flag, connection, allowed methods, and the first failing gate.</summary>
		Task<OnlinePaymentsStatus> GetStatusAsync(int departmentId);

		// ---- Connect (department administrators only; the caller enforces the role) ----------------------------

		/// <summary>Signed, single-use, 15-minute state plus the provider's authorize URL. Refuses while the cluster switch is off.</summary>
		Task<PaymentConnectBegin> BeginConnectAsync(int departmentId, int provider, string userId, string ipAddress, string userAgent);

		/// <summary>Validates the state, exchanges the code, reads the account, upserts the connection (default when first), audits and e-mails the department administrators.</summary>
		Task<DepartmentPaymentConnection> CompleteConnectAsync(int departmentId, int provider, IReadOnlyDictionary<string, string> callbackParameters, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		/// <summary>Deauthorizes at the provider and marks the connection Disconnected; open requests are cancelled.</summary>
		Task<bool> DisconnectAsync(string departmentPaymentConnectionId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		Task<IReadOnlyList<DepartmentPaymentConnection>> GetConnectionsAsync(int departmentId);

		// ---- Pay links --------------------------------------------------------------------------------------------

		/// <summary>
		/// Reuses the invoice's open request while unexpired, else opens a hosted request for the full remaining
		/// balance. Refuses Draft/Void/Paid invoices, a zero balance, a lapsed entitlement, either flag off, a missing
		/// or not-ready connection, or a currency the account does not settle.
		/// </summary>
		Task<InvoicePaymentRequest> CreatePaymentRequestAsync(string invoiceId, int departmentId, int source, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		Task<InvoicePaymentRequest> GetOpenRequestAsync(string invoiceId, int departmentId);
		Task<IReadOnlyList<InvoicePaymentRequest>> GetRequestsAsync(string invoiceId, int departmentId);

		/// <summary>{PublicBaseUrl}/pay/{token} for the invoice, valid for the department's pay-link expiry; null when online payment is not offered right now.</summary>
		Task<string> BuildPayPageUrlAsync(string invoiceId, int departmentId);

		/// <summary>The pay-page token itself (p|department|invoice|expiry, encrypted, URL safe).</summary>
		string CreatePayPageToken(int departmentId, string invoiceId, DateTime expiresOnUtc);

		/// <summary>What the anonymous pay page shows; Available is false with a reason for an expired or foreign token.</summary>
		Task<PayPageModel> GetPayPageModelAsync(string token);

		// ---- Truth ------------------------------------------------------------------------------------------------

		/// <summary>Verifies, records and applies one webhook body; never throws. Accepted answers 2xx, otherwise 400.</summary>
		Task<PaymentWebhookReceipt> ReceiveWebhookAsync(int provider, string signatureHeader, string rawBody, string ipAddress, CancellationToken cancellationToken = default);

		/// <summary>Applies a parsed envelope idempotently (event id). The reconciliation pass and the webhook share this path.</summary>
		Task<PaymentEventOutcomes> ApplyProviderEventAsync(PaymentProviderEventEnvelope envelope, string rawBody, CancellationToken cancellationToken = default);

		// ---- Worker 29 passes 2–4 ---------------------------------------------------------------------------------

		/// <summary>Reads back every open request older than RequestReconcileAfterMinutes and applies its state (a lost webhook lands within one cycle). Returns the number applied.</summary>
		Task<int> ReconcileOpenRequestsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default);

		/// <summary>Open requests past ExpiresOn become Expired. Returns the count.</summary>
		Task<int> ExpireStaleRequestsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default);

		/// <summary>Re-reads connections not verified in a day; a revoked or charges-disabled account is downgraded. Returns the count checked.</summary>
		Task<int> ReverifyConnectionsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default);

		/// <summary>Deletes event bodies older than EventRetentionDays. Returns the count.</summary>
		Task<int> PurgeEventsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default);

		// ---- Health -----------------------------------------------------------------------------------------------

		/// <summary>Health of the webhook path for the v4 Health endpoint (plan B2.5a). Never throws; a cluster with payment collection off reports healthy.</summary>
		Task<PaymentsWebhookHealth> GetWebhookHealthAsync();
	}
}
