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

		/// <summary>
		/// Generated hourly unit lines: where the on-scene time came from (InvoiceLineTimeSources: 1 the unit's own status,
		/// 2 a status Resgrid auto-linked to the call, 3 a status inferred for the call, 4 the call window). Null otherwise.
		/// </summary>
		public int? TimeSource { get; set; }
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

	// ---- Phase B2 online payments -------------------------------------------------------------------------------

	public class OnlinePaymentsStatusResult : StandardApiResponseV4Base
	{
		public OnlinePaymentsStatusData Data { get; set; }
	}

	public class OnlinePaymentsStatusData
	{
		/// <summary>Payment collection is offered in this cluster (config switch and Payments.StripeConnect flag).</summary>
		public bool AvailableInCluster { get; set; }
		/// <summary>Invoicing.OnlinePayments evaluates true for the department.</summary>
		public bool FlagEnabled { get; set; }
		/// <summary>The department turned online payments on in its billing settings.</summary>
		public bool EnabledByDepartment { get; set; }
		/// <summary>A pay link can be created right now.</summary>
		public bool CanCollect { get; set; }
		/// <summary>The first failing gate (payments_*), or null.</summary>
		public string BlockedReason { get; set; }
		public string[] AllowedPaymentMethods { get; set; } = new string[0];
		public PaymentConnectionData Connection { get; set; }
	}

	public class PaymentConnectionsResult : StandardApiResponseV4Base
	{
		public List<PaymentConnectionData> Data { get; set; } = new List<PaymentConnectionData>();
	}

	/// <summary>A connection as the apps may see it: masked account id, no secrets (plan B2.6).</summary>
	public class PaymentConnectionData
	{
		public string DepartmentPaymentConnectionId { get; set; }
		public int Provider { get; set; }
		public string ProviderName { get; set; }
		public int Status { get; set; }
		public string StatusName { get; set; }
		public int Environment { get; set; }
		public string MaskedAccountId { get; set; }
		public string DisplayName { get; set; }
		public string Country { get; set; }
		public string DefaultCurrency { get; set; }
		public bool IsDefault { get; set; }
		public DateTime ConnectedOn { get; set; }
		public DateTime? LastVerifiedOn { get; set; }
	}

	public class CreatePaymentLinkInput
	{
		public string InvoiceId { get; set; }
	}

	public class PaymentLinkResult : StandardApiResponseV4Base
	{
		public PaymentLinkData Data { get; set; }
	}

	public class PaymentLinkData
	{
		public string InvoiceId { get; set; }
		/// <summary>The stable Resgrid pay page for the invoice (share this; it survives provider session expiry).</summary>
		public string PayUrl { get; set; }
		public string InvoicePaymentRequestId { get; set; }
		public int RequestStatus { get; set; }
		public string RequestStatusName { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }
		public DateTime ExpiresOn { get; set; }
	}
}
