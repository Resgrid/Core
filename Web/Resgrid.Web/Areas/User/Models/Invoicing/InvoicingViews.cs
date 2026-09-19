using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Models.Invoicing
{
	/// <summary>Shared page state: whether the department may write (live add-on) and the customer names the page needs.</summary>
	public abstract class InvoicingPageView
	{
		/// <summary>False when the Business Operations add-on window has lapsed: invoices stay readable, every mutation is refused (plan decision 42).</summary>
		public bool CanWrite { get; set; }
		public bool CanManage { get; set; }
		/// <summary>ContactId → display name, already resolved for Advanced Data Protection (REDACTED when the caller holds no grant).</summary>
		public Dictionary<string, string> ContactNames { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public string ContactName(string contactId) => contactId != null && ContactNames.TryGetValue(contactId, out var name) ? name : null;
	}

	public sealed class InvoiceIndexView : InvoicingPageView
	{
		public List<Invoice> Invoices { get; set; } = new List<Invoice>();
		public int? Status { get; set; }
		public string ContactId { get; set; }
		public int Page { get; set; }
		public int PageSize { get; set; } = 50;
		public int TotalCount { get; set; }
		/// <summary>Open balance per currency code; invoices may be issued in any supported currency, so unlike codes are never summed.</summary>
		public IReadOnlyDictionary<string, decimal> OutstandingBalances { get; set; } = new Dictionary<string, decimal>();
		public IReadOnlyDictionary<string, decimal> OverdueBalances { get; set; } = new Dictionary<string, decimal>();
		public int OverdueCount { get; set; }

		/// <summary>"1,234.00 USD / 950.00 EUR" one per line; "0.00" when nothing is open. Shared by the index cards and the aging table.</summary>
		public static string FormatBalances(IReadOnlyDictionary<string, decimal> balances)
		{
			if (balances == null || balances.Count == 0)
				return 0m.ToString("N2");
			return string.Join(" / ", balances.OrderBy(b => b.Key, StringComparer.OrdinalIgnoreCase).Select(b => string.IsNullOrEmpty(b.Key) ? b.Value.ToString("N2") : b.Value.ToString("N2") + " " + b.Key));
		}
	}

	public sealed class InvoiceNewView : InvoicingPageView
	{
		public string ContactId { get; set; }
		public string Currency { get; set; } = "USD";
		/// <summary>Contacts with a billing profile first, then every other active contact.</summary>
		public List<InvoiceCustomerChoice> Customers { get; set; } = new List<InvoiceCustomerChoice>();
		public string Message { get; set; }
	}

	public sealed class InvoiceCustomerChoice
	{
		public string ContactId { get; set; }
		public string Name { get; set; }
		public bool HasBillingProfile { get; set; }
	}

	public sealed class InvoiceEditView : InvoicingPageView
	{
		public Invoice Invoice { get; set; }
		public CustomerBillingProfile Profile { get; set; }
		public List<RateCard> RateCards { get; set; } = new List<RateCard>();
		public string DefaultRateCardId { get; set; }
		public string Message { get; set; }
	}

	public sealed class InvoiceDetailView : InvoicingPageView
	{
		public Invoice Invoice { get; set; }
		public CustomerBillingProfile Profile { get; set; }
		public string RenderedHtml { get; set; }
		public string Message { get; set; }
		/// <summary>Phase B2: null when the cluster does not offer payment collection at all (the section is absent).</summary>
		public OnlinePaymentsStatus OnlinePayments { get; set; }
		/// <summary>The shareable pay-page link, or null when online payment is not offered for this invoice right now.</summary>
		public string PayUrl { get; set; }
		public InvoicePaymentRequest OpenRequest { get; set; }
		public IReadOnlyList<InvoicePaymentRequest> PaymentRequests { get; set; } = new List<InvoicePaymentRequest>();
	}

	public sealed class RateCardsView : InvoicingPageView
	{
		public List<RateCard> RateCards { get; set; } = new List<RateCard>();
	}

	public sealed class RateCardEditView : InvoicingPageView
	{
		public RateCard RateCard { get; set; } = new RateCard();
		public List<RateCardItem> Items { get; set; } = new List<RateCardItem>();
		public List<string> UnitTypes { get; set; } = new List<string>();
		public string Message { get; set; }
	}

	public sealed class RateCardInput
	{
		public string RateCardId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public bool IsDefault { get; set; }
		public bool Active { get; set; } = true;
	}

	public sealed class RateCardItemInput
	{
		public string RateCardItemId { get; set; }
		public string RateCardId { get; set; }
		public int ItemType { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public decimal Rate { get; set; }
		public string UnitLabel { get; set; }
		public int? MinimumMinutes { get; set; }
		public int? RoundingMinutes { get; set; }
		public decimal? MinimumCharge { get; set; }
		public string UnitTypeFilter { get; set; }
		public bool Taxable { get; set; } = true;
		public int SortOrder { get; set; }
		public bool Active { get; set; } = true;
	}

	public sealed class InvoiceAgingView : InvoicingPageView
	{
		public InvoiceAgingReport Report { get; set; }
	}

	public sealed class BillingSettingsView : InvoicingPageView
	{
		public DepartmentBillingIdentity Identity { get; set; } = new DepartmentBillingIdentity();
		public Address RemitTo { get; set; } = new Address();
		/// <summary>Phase B2: the department's online-payments posture (cluster switch, flag, connection, gates).</summary>
		public OnlinePaymentsStatus OnlinePayments { get; set; } = new OnlinePaymentsStatus();
		public bool OnlinePaymentsClusterEnabled => OnlinePayments?.AvailableInCluster == true;
		public bool OnlinePaymentsFlagEnabled => OnlinePayments?.FlagEnabled == true;
		/// <summary>"identity" or "online"; the online tab is requested by the action catalog and after a connect round-trip.</summary>
		public string ActiveTab { get; set; } = "identity";
		public bool IsDepartmentAdmin { get; set; }
		public bool SaveSuccess { get; set; }
		public string Message { get; set; }
	}

	public sealed class OnlinePaymentsInput
	{
		public bool OnlinePaymentsEnabled { get; set; }
		/// <summary>Subset of "card", "us_bank_account".</summary>
		public string[] AllowedPaymentMethods { get; set; } = new string[0];
	}

	public sealed class BillingSettingsInput
	{
		public string LegalBusinessName { get; set; }
		public string TaxRegistrationNumber { get; set; }
		public string SecondaryTaxRegistrationNumber { get; set; }
		public string SamUei { get; set; }
		public string CageCode { get; set; }
		public string WorkersCompAccountNumber { get; set; }
		public string InvoiceFooterText { get; set; }
		public int PayLinkExpiryDays { get; set; } = 30;
		public bool ShowPayOnlineOnDocuments { get; set; } = true;
		public string Address1 { get; set; }
		public string City { get; set; }
		public string State { get; set; }
		public string PostalCode { get; set; }
		public string Country { get; set; }
	}

	public sealed class BillingProfileView : InvoicingPageView
	{
		public Contact Contact { get; set; }
		public CustomerBillingProfile Profile { get; set; } = new CustomerBillingProfile();
		public Address BillingAddress { get; set; } = new Address();
		public List<TaxComponent> TaxComponents { get; set; } = new List<TaxComponent>();
		public List<RateCard> RateCards { get; set; } = new List<RateCard>();
		public List<Invoice> Invoices { get; set; } = new List<Invoice>();
		public bool IsProtectedContact { get; set; }
		public bool SaveSuccess { get; set; }
		public string Message { get; set; }
	}

	public sealed class BillingProfileInput
	{
		public string ContactId { get; set; }
		public string BillingEmail { get; set; }
		public bool UseContactMailingAddress { get; set; } = true;
		public string Address1 { get; set; }
		public string City { get; set; }
		public string State { get; set; }
		public string PostalCode { get; set; }
		public string Country { get; set; }
		public int TermsNetDays { get; set; } = 30;
		public bool TaxExempt { get; set; }
		public decimal? TaxRate { get; set; }
		/// <summary>Up to three named tax components; a row with an empty name is ignored.</summary>
		public string[] TaxName { get; set; } = new string[0];
		public decimal?[] TaxPercent { get; set; } = new decimal?[0];
		public string[] TaxRegistration { get; set; } = new string[0];
		public string DefaultRateCardId { get; set; }
		public decimal? DefaultDiscountPercent { get; set; }
		public bool PurchaseOrderRequired { get; set; }
		public string Notes { get; set; }
		public bool Active { get; set; } = true;
	}

	public sealed class InvoiceHeaderInput
	{
		public string InvoiceId { get; set; }
		public DateTime? DueOn { get; set; }
		public decimal? DiscountPercent { get; set; }
		public string Currency { get; set; }
		public string Notes { get; set; }
		public string TermsText { get; set; }
		/// <summary>JSON array of <see cref="InvoiceLineInput"/>; the whole draft line list is replaced on save.</summary>
		public string LinesJson { get; set; }
	}

	public sealed class InvoiceLineInput
	{
		public string InvoiceLineItemId { get; set; }
		public int? CallId { get; set; }
		public string RateCardItemId { get; set; }
		public string Description { get; set; }
		public decimal Quantity { get; set; }
		public decimal UnitRate { get; set; }
		public bool Taxable { get; set; } = true;
	}

	public sealed class RecordPaymentInput
	{
		public string InvoiceId { get; set; }
		public decimal Amount { get; set; }
		public int Method { get; set; }
		public string Reference { get; set; }
		public string Notes { get; set; }
		public DateTime? PaidOn { get; set; }
	}

	/// <summary>Row of the "Add call" dialog.</summary>
	public sealed class InvoiceCallChoice
	{
		public int CallId { get; set; }
		public string Number { get; set; }
		public string Name { get; set; }
		public DateTime LoggedOn { get; set; }
		public bool AlreadyInvoiced { get; set; }
	}
}
