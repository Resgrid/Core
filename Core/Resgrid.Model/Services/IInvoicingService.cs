using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;

namespace Resgrid.Model.Services
{
	/// <summary>One bucket of the accounts-receivable aging report (plan B4).</summary>
	public class InvoiceAgingBucket
	{
		public string Label { get; set; }
		public int Count { get; set; }
		public decimal Balance { get; set; }
		public List<InvoiceAgingRow> Invoices { get; set; } = new List<InvoiceAgingRow>();
	}

	/// <summary>Accounts-receivable aging: current / 1–30 / 31–60 / 61–90 / 90+ days past due.</summary>
	public class InvoiceAgingReport
	{
		public DateTime AsOfUtc { get; set; }
		public List<InvoiceAgingBucket> Buckets { get; set; } = new List<InvoiceAgingBucket>();
		public decimal TotalBalance { get; set; }
		public int TotalCount { get; set; }
	}

	/// <summary>The department's Phase B invoicing surface (Workforce &amp; Business Operations plan, B4). Every mutation audits; lifecycle transitions publish their Workflow trigger through the domain outbox.</summary>
	public interface IInvoicingService
	{
		// Billing profiles
		Task<CustomerBillingProfile> GetBillingProfileByContactIdAsync(string contactId, int departmentId);
		Task<CustomerBillingProfile> GetBillingProfileByIdAsync(string customerBillingProfileId, int departmentId);
		Task<List<CustomerBillingProfile>> GetBillingProfilesForDepartmentAsync(int departmentId);
		Task<CustomerBillingProfile> SaveBillingProfileAsync(CustomerBillingProfile profile, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteBillingProfileAsync(string customerBillingProfileId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>True when the contact has a billing profile with any non-void invoice (the contact delete guard, plan risk 6).</summary>
		Task<bool> ContactHasOpenBillingAsync(string contactId, int departmentId);

		// Rate cards
		Task<List<RateCard>> GetRateCardsForDepartmentAsync(int departmentId);
		Task<RateCard> GetRateCardByIdAsync(string rateCardId, int departmentId, bool includeInactiveItems = false);
		Task<RateCard> SaveRateCardAsync(RateCard rateCard, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteRateCardAsync(string rateCardId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<RateCardItem> SaveRateCardItemAsync(RateCardItem item, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteRateCardItemAsync(string rateCardItemId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>The contact's pinned card, else the department default, else null.</summary>
		Task<RateCard> GetEffectiveRateCardForContactAsync(string contactId, int departmentId);

		// Invoices
		Task<List<Invoice>> GetInvoicesForDepartmentAsync(int departmentId, InvoiceListFilter filter);
		Task<int> CountInvoicesForDepartmentAsync(int departmentId, InvoiceListFilter filter);
		Task<List<Invoice>> GetInvoicesByContactIdAsync(string contactId, int departmentId);
		/// <summary>The invoice with its line items and payments loaded.</summary>
		Task<Invoice> GetInvoiceByIdAsync(string invoiceId, int departmentId);
		/// <summary>Assigns the next number, applies the profile's terms and default discount, and audits (Draft).</summary>
		Task<Invoice> CreateDraftInvoiceAsync(int departmentId, string contactId, string userId, string ipAddress, string userAgent, string currency = null, CancellationToken cancellationToken = default);
		/// <summary>Draft-only edit of header fields (notes, terms, discount, due date, currency); totals are recomputed.</summary>
		Task<Invoice> SaveInvoiceAsync(Invoice invoice, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Replaces a draft's line items with the supplied list and recomputes totals.</summary>
		Task<Invoice> SaveInvoiceLineItemsAsync(string invoiceId, int departmentId, List<InvoiceLineItem> lineItems, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Editable draft lines for a call from a rate card: time on scene from unit states (plan decision 9), flat and fixed items. Nothing is saved.</summary>
		Task<List<InvoiceLineItem>> GenerateLineItemsFromCallAsync(int callId, string rateCardId, int departmentId);
		/// <summary>Appends the generated lines for a call to a draft invoice and recomputes totals.</summary>
		Task<Invoice> AddCallToInvoiceAsync(string invoiceId, int callId, string rateCardId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>SubTotal → discount → tax (per component when TaxComponentsJson is present, else the flat rate) → Total (plan B4).</summary>
		Task<Invoice> RecalculateTotalsAsync(string invoiceId, int departmentId, CancellationToken cancellationToken = default);
		/// <summary>Draft → Sent: sets IssuedOn, DueOn from the profile's terms when unset, SentOn/SentToEmail. Delivery (e-mail + PDF) is a separate step.</summary>
		Task<Invoice> MarkSentAsync(string invoiceId, int departmentId, string sentToEmail, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<Invoice> VoidInvoiceAsync(string invoiceId, int departmentId, string reason, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>The one choke point for every payment, manual or online: updates AmountPaid and the Partial/Paid transition (plan B4).</summary>
		Task<InvoicePayment> RecordPaymentAsync(InvoicePayment payment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Applies a refund (or lost dispute) to a recorded payment and re-derives the invoice's status and balance (Phase B2).</summary>
		Task<InvoicePayment> ApplyPaymentRefundAsync(string invoicePaymentId, int departmentId, decimal refundedAmount, bool disputeLost, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Sent / PartiallyPaid invoices past DueOn become Overdue (the invoice maintenance worker's pass). Returns the number transitioned. <paramref name="departmentEnabled"/> lets the worker skip departments whose entitlement lapsed.</summary>
		Task<int> MarkOverdueInvoicesAsync(DateTime asOfUtc, Func<int, Task<bool>> departmentEnabled = null, CancellationToken cancellationToken = default);
		Task<InvoiceAgingReport> GetAccountsReceivableAgingAsync(int departmentId, DateTime? asOfUtc = null);

		// Rendering and delivery (plan B4)
		/// <summary>A self-contained HTML rendering of the invoice: department billing identity, customer, lines, discount line, each named tax component with its registration number, totals, terms. Never includes internal cost.</summary>
		Task<string> RenderInvoiceHtmlAsync(string invoiceId, int departmentId);
		/// <summary>The HTML rendering converted through IPdfProvider; null when the invoice is not found.</summary>
		Task<byte[]> GetInvoicePdfAsync(string invoiceId, int departmentId);
		/// <summary>E-mails the invoice PDF to the customer (to the address given, else the billing profile's e-mail). A Draft is marked Sent first; a Sent invoice is re-sent without a status change. Returns the invoice.</summary>
		Task<Invoice> SendInvoiceAsync(string invoiceId, int departmentId, string toEmail, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		// Department billing identity
		Task<DepartmentBillingIdentity> GetDepartmentBillingIdentityAsync(int departmentId);
		Task<DepartmentBillingIdentity> SaveDepartmentBillingIdentityAsync(DepartmentBillingIdentity identity, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
	}
}
