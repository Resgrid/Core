using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;

namespace Resgrid.Model.Repositories
{
	/// <summary>Department payment-provider connections (plan B2.2, registry M0212).</summary>
	public interface IDepartmentPaymentConnectionRepository : IRepository<DepartmentPaymentConnection>
	{
		Task<DepartmentPaymentConnection> GetByIdForDepartmentAsync(string departmentPaymentConnectionId, int departmentId);
		Task<IEnumerable<DepartmentPaymentConnection>> GetForDepartmentAsync(int departmentId);
		/// <summary>The department's default usable connection, else its newest Connected one, else null.</summary>
		Task<DepartmentPaymentConnection> GetDefaultForDepartmentAsync(int departmentId);
		/// <summary>Live (not deleted) connection for a provider account, in any department; how a webhook finds its department.</summary>
		Task<DepartmentPaymentConnection> GetByExternalAccountIdAsync(int provider, string externalAccountId);
		/// <summary>Clears IsDefault on every other live connection of the department.</summary>
		Task<int> ClearDefaultAsync(int departmentId, string exceptConnectionId, CancellationToken cancellationToken = default);
		/// <summary>Connected connections not verified since <paramref name="notVerifiedSinceUtc"/> (the worker's re-verification pass).</summary>
		Task<IEnumerable<DepartmentPaymentConnection>> GetStaleVerifiedAsync(DateTime notVerifiedSinceUtc, int take);
	}

	/// <summary>Hosted payment requests (plan B2.2).</summary>
	public interface IInvoicePaymentRequestRepository : IRepository<InvoicePaymentRequest>
	{
		Task<InvoicePaymentRequest> GetByIdForDepartmentAsync(string invoicePaymentRequestId, int departmentId);
		Task<IEnumerable<InvoicePaymentRequest>> GetByInvoiceIdAsync(string invoiceId, int departmentId);
		/// <summary>The one non-terminal request for an invoice, or null.</summary>
		Task<InvoicePaymentRequest> GetOpenByInvoiceIdAsync(string invoiceId, int departmentId);
		Task<InvoicePaymentRequest> GetByExternalReferenceAsync(int provider, string externalReference);
		Task<InvoicePaymentRequest> GetByPaymentIntentIdAsync(int provider, string paymentIntentId);
		/// <summary>Open requests created before <paramref name="createdBeforeUtc"/> (the worker's reconciliation pass).</summary>
		Task<IEnumerable<InvoicePaymentRequest>> GetOpenOlderThanAsync(DateTime createdBeforeUtc, int take);
		/// <summary>Open requests whose ExpiresOn is before <paramref name="asOfUtc"/>.</summary>
		Task<IEnumerable<InvoicePaymentRequest>> GetOpenExpiredAsync(DateTime asOfUtc, int take);
		/// <summary>Open requests of one connection (cancelled when the connection is revoked).</summary>
		Task<IEnumerable<InvoicePaymentRequest>> GetOpenByConnectionAsync(string departmentPaymentConnectionId);
		/// <summary>Health: open requests created before <paramref name="createdBeforeUtc"/>.</summary>
		Task<int> CountOpenOlderThanAsync(DateTime createdBeforeUtc);
		/// <summary>Health: a request became Completed or Processing since <paramref name="sinceUtc"/>.</summary>
		Task<bool> HasActivitySinceAsync(DateTime sinceUtc);
	}

	/// <summary>Received provider events (plan B2.2): idempotency ledger and forensic bodies.</summary>
	public interface IPaymentProviderEventRepository : IRepository<PaymentConnectEvent>
	{
		Task<PaymentConnectEvent> GetByExternalEventIdAsync(int provider, string externalEventId);
		/// <summary>Health: newest ReceivedOn, or null.</summary>
		Task<DateTime?> GetNewestReceivedOnAsync();
		/// <summary>Health: newest ProcessedOn among Applied events, or null.</summary>
		Task<DateTime?> GetNewestAppliedOnAsync();
		/// <summary>Health: events with the outcome received since <paramref name="sinceUtc"/>.</summary>
		Task<int> CountByOutcomeSinceAsync(int outcome, DateTime sinceUtc);
		/// <summary>Worker: deletes events received before <paramref name="receivedBeforeUtc"/>; returns the count.</summary>
		Task<int> PurgeReceivedBeforeAsync(DateTime receivedBeforeUtc, CancellationToken cancellationToken = default);
	}
}
