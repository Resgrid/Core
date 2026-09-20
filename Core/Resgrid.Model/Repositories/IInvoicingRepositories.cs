using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;

namespace Resgrid.Model.Repositories
{
	/// <summary>Customer billing profiles (plan B3).</summary>
	public interface ICustomerBillingProfileRepository : IRepository<CustomerBillingProfile>
	{
		Task<CustomerBillingProfile> GetByIdForDepartmentAsync(string customerBillingProfileId, int departmentId);
		Task<CustomerBillingProfile> GetByContactIdAsync(string contactId, int departmentId);
		Task<IEnumerable<CustomerBillingProfile>> GetByContactIdsAsync(int departmentId, IEnumerable<string> contactIds);
		Task<IEnumerable<CustomerBillingProfile>> GetAllForDepartmentAsync(int departmentId);
	}

	/// <summary>Rate cards (plan B3).</summary>
	public interface IRateCardRepository : IRepository<RateCard>
	{
		Task<RateCard> GetByIdForDepartmentAsync(string rateCardId, int departmentId);
		Task<IEnumerable<RateCard>> GetAllForDepartmentAsync(int departmentId);
		Task<RateCard> GetDefaultForDepartmentAsync(int departmentId);
		/// <summary>Clears IsDefault on every other live card of the department.</summary>
		Task<int> ClearDefaultAsync(int departmentId, string exceptRateCardId, CancellationToken cancellationToken = default);
	}

	/// <summary>Rate card items (plan B3).</summary>
	public interface IRateCardItemRepository : IRepository<RateCardItem>
	{
		Task<RateCardItem> GetByIdForDepartmentAsync(string rateCardItemId, int departmentId);
		Task<IEnumerable<RateCardItem>> GetByRateCardIdAsync(string rateCardId, int departmentId, bool includeInactive = false);
	}

	/// <summary>A row of the accounts-receivable aging query: one open invoice with its balance and days past due.</summary>
	public class InvoiceAgingRow
	{
		public string InvoiceId { get; set; }
		public int InvoiceNumber { get; set; }
		public string ContactId { get; set; }
		public int Status { get; set; }
		public DateTime? DueOn { get; set; }
		public string Currency { get; set; }
		public decimal Total { get; set; }
		public decimal AmountPaid { get; set; }
		public decimal Balance => Total - AmountPaid;
	}

	/// <summary>Filter for the department invoice list.</summary>
	public class InvoiceListFilter
	{
		public IEnumerable<int> Statuses { get; set; }
		public string ContactId { get; set; }
		/// <summary>Invoices generated under one service contract (Phase C contractor billing).</summary>
		public string ServiceContractId { get; set; }
		public DateTime? IssuedFromUtc { get; set; }
		public DateTime? IssuedToUtc { get; set; }
		public int Skip { get; set; }
		public int Take { get; set; } = 50;
	}

	/// <summary>Invoices (plan B3).</summary>
	public interface IInvoiceRepository : IRepository<Invoice>
	{
		Task<Invoice> GetByIdForDepartmentAsync(string invoiceId, int departmentId);
		Task<Invoice> GetByNumberAsync(int departmentId, int invoiceNumber);
		Task<IEnumerable<Invoice>> GetForDepartmentAsync(int departmentId, InvoiceListFilter filter);
		Task<int> CountForDepartmentAsync(int departmentId, InvoiceListFilter filter);
		Task<IEnumerable<Invoice>> GetByContactIdAsync(string contactId, int departmentId);
		Task<IEnumerable<Invoice>> GetInvoicesByStatusAsync(int departmentId, int status);
		/// <summary>Sent or PartiallyPaid invoices, in any department, whose DueOn is before <paramref name="asOfUtc"/> (the worker's overdue sweep).</summary>
		Task<IEnumerable<Invoice>> GetOverdueCandidatesAsync(DateTime asOfUtc, int take);
		/// <summary>Open (Sent / PartiallyPaid / Overdue) invoices of a department with their balances.</summary>
		Task<IEnumerable<InvoiceAgingRow>> GetAgingDataAsync(int departmentId);
		/// <summary>True when the contact has any invoice that is not Void and not deleted (contact delete guard, plan risk 6).</summary>
		Task<bool> HasNonVoidInvoicesForContactAsync(string contactId, int departmentId);
	}

	/// <summary>Invoice line items (plan B3). Lines have no soft delete: a draft's lines are replaced.</summary>
	public interface IInvoiceLineItemRepository : IRepository<InvoiceLineItem>
	{
		Task<IEnumerable<InvoiceLineItem>> GetByInvoiceIdAsync(string invoiceId, int departmentId);
		Task<IEnumerable<InvoiceLineItem>> GetByCallIdAsync(int callId, int departmentId);
		Task<int> DeleteByInvoiceIdAsync(string invoiceId, int departmentId, CancellationToken cancellationToken = default);
		Task<int> DeleteByIdAsync(string invoiceLineItemId, int departmentId, CancellationToken cancellationToken = default);
	}

	/// <summary>Invoice payments (plan B3).</summary>
	public interface IInvoicePaymentRepository : IRepository<InvoicePayment>
	{
		Task<InvoicePayment> GetByIdForDepartmentAsync(string invoicePaymentId, int departmentId);
		Task<IEnumerable<InvoicePayment>> GetByInvoiceIdAsync(string invoiceId, int departmentId);
		Task<InvoicePayment> GetByGatewayTransactionIdAsync(int provider, string gatewayTransactionId);
	}

	/// <summary>Per-department invoice numbering (plan decision 7).</summary>
	public interface IInvoiceNumberSequenceRepository : IRepository<InvoiceNumberSequence>
	{
		/// <summary>Atomically returns the next number for the department and advances the sequence (dialect-specific SQL).</summary>
		Task<int> GetNextNumberAsync(int departmentId, CancellationToken cancellationToken = default);
	}

	/// <summary>The department's billing identity (plan B1).</summary>
	public interface IDepartmentBillingIdentityRepository : IRepository<DepartmentBillingIdentity>
	{
		Task<DepartmentBillingIdentity> GetByDepartmentIdAsync(int departmentId);
		/// <summary>Insert-or-update keyed by DepartmentId.</summary>
		Task<DepartmentBillingIdentity> UpsertAsync(DepartmentBillingIdentity identity, CancellationToken cancellationToken = default);
	}
}
