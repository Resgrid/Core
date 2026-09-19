using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Invoicing
{
	/// <summary>Whether this department may use customer invoicing right now (flag and Business Operations entitlement) and what it costs.</summary>
	public class InvoicingAccessResult : StandardApiResponseV4Base
	{
		public InvoicingAccessData Data { get; set; }
	}

	public class InvoicingAccessData
	{
		/// <summary>Invoicing.CustomerInvoicing is on for the department (operator rollout).</summary>
		public bool FlagEnabled { get; set; }
		/// <summary>The department holds an active Business Operations add-on.</summary>
		public bool AddonActive { get; set; }
		/// <summary>Both of the above: invoicing endpoints answer.</summary>
		public bool InvoicingEnabled { get; set; }
		/// <summary>The caller may create, edit, send and void invoices and record payments.</summary>
		public bool CanManage { get; set; }
		public string ProductName { get; set; } = "Business Operations";
		public string BillingInterval { get; set; } = "month";
		public InvoicingOffer[] Offers { get; set; } = new[]
		{
			new InvoicingOffer { Region = "US", Provider = "Stripe", Currency = "USD", MonthlyAmount = Config.BusinessOperationsAddonConfig.StripeMonthlyAmount },
			new InvoicingOffer { Region = "EU", Provider = "Paddle", Currency = "EUR", MonthlyAmount = Config.BusinessOperationsAddonConfig.PaddleMonthlyAmount }
		};
	}

	public class InvoicingOffer
	{
		public string Region { get; set; }
		public string Provider { get; set; }
		public string Currency { get; set; }
		public decimal MonthlyAmount { get; set; }
	}

	public class InvoicesResult : StandardApiResponseV4Base
	{
		public List<InvoiceResultData> Data { get; set; } = new List<InvoiceResultData>();
		/// <summary>Total matching invoices for the filter (paging).</summary>
		public int TotalCount { get; set; }
	}

	public class InvoiceResult : StandardApiResponseV4Base
	{
		public InvoiceResultData Data { get; set; }
	}

	/// <summary>An invoice header. Money is decimal; the status is the InvoiceStatus value (Draft 0, Sent 1, PartiallyPaid 2, Paid 3, Overdue 4, Void 5).</summary>
	public class InvoiceResultData
	{
		public string InvoiceId { get; set; }
		public int InvoiceNumber { get; set; }
		public string ContactId { get; set; }
		/// <summary>The contact's display name; REDACTED on a protected row.</summary>
		public string ContactName { get; set; }
		public int Status { get; set; }
		public string StatusName { get; set; }
		public string Currency { get; set; }
		public decimal SubTotal { get; set; }
		public decimal? DiscountPercent { get; set; }
		public decimal DiscountAmount { get; set; }
		public decimal TaxAmount { get; set; }
		public decimal Total { get; set; }
		public decimal AmountPaid { get; set; }
		public decimal Balance { get; set; }
		public DateTime? IssuedOn { get; set; }
		public DateTime? DueOn { get; set; }
		public DateTime? SentOn { get; set; }
		public DateTime? PaidOn { get; set; }
		public DateTime? VoidedOn { get; set; }
		public string Notes { get; set; }
		public string TermsText { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime? UpdatedOn { get; set; }
		/// <summary>Populated by GetInvoice only.</summary>
		public List<InvoiceLineItemData> LineItems { get; set; }
		/// <summary>Populated by GetInvoice only.</summary>
		public List<InvoicePaymentData> Payments { get; set; }
	}

	public class InvoiceLineItemData
	{
		public string InvoiceLineItemId { get; set; }
		public int? CallId { get; set; }
		public string Description { get; set; }
		public decimal Quantity { get; set; }
		public decimal UnitRate { get; set; }
		public decimal Amount { get; set; }
		public bool Taxable { get; set; }
		public int SortOrder { get; set; }
	}

	public class InvoicePaymentData
	{
		public string InvoicePaymentId { get; set; }
		public decimal Amount { get; set; }
		/// <summary>InvoicePaymentMethods value (Check 0, Cash 1, Ach 2, CardExternal 3, Other 4, Online 5).</summary>
		public int Method { get; set; }
		public string MethodName { get; set; }
		/// <summary>InvoicePaymentStatuses value (Succeeded 0, Refunded 1, PartiallyRefunded 2, Disputed 3, DisputeLost 4).</summary>
		public int Status { get; set; }
		public decimal RefundedAmount { get; set; }
		public string Reference { get; set; }
		/// <summary>Display-only summary such as "Visa •••• 4242"; never a card number.</summary>
		public string PaymentMethodSummary { get; set; }
		public DateTime PaidOn { get; set; }
		public DateTime AddedOn { get; set; }
	}

	/// <summary>Record a manual payment against a Sent, PartiallyPaid or Overdue invoice.</summary>
	public class RecordPaymentInput
	{
		public string InvoiceId { get; set; }
		public decimal Amount { get; set; }
		/// <summary>InvoicePaymentMethods value; Online (5) is reserved for the provider path and is refused here.</summary>
		public int Method { get; set; }
		public string Reference { get; set; }
		public string Notes { get; set; }
		/// <summary>UTC; defaults to now.</summary>
		public DateTime? PaidOn { get; set; }
	}
}
