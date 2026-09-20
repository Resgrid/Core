using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Invoicing
{
	/// <summary>Live or sandbox keys behind a connection (plan B2.2).</summary>
	public enum PaymentEnvironments
	{
		Live = 0,
		Sandbox = 1
	}

	/// <summary>Where a payment request was started (plan B2.2).</summary>
	public enum PaymentRequestSources
	{
		PayPage = 0,
		Web = 1,
		Api = 2
	}

	/// <summary>What the receiver did with a provider webhook event (plan B2.2).</summary>
	public enum PaymentEventOutcomes
	{
		Applied = 0,
		Ignored = 1,
		Duplicate = 2,
		Failed = 3,
		Rejected = 4
	}

	/// <summary>Stages of a card dispute against an online payment (plan B2.4): opened flags the payment and fires trigger 95 once; won restores it; lost is treated as a refund.</summary>
	public enum InvoiceDisputeStages
	{
		Opened = 0,
		Won = 1,
		Lost = 2
	}

	/// <summary>Provider-neutral meaning of a webhook event (plan B2.3).</summary>
	public enum PaymentEventKinds
	{
		Unknown = 0,
		ConnectionRevoked = 1,
		ConnectionUpdated = 2,
		PaymentProcessing = 3,
		PaymentSucceeded = 4,
		PaymentFailed = 5,
		RequestExpired = 6,
		PaymentRefunded = 7,
		PaymentDisputed = 8,
		DisputeClosed = 9
	}

	/// <summary>
	/// A department's own payment-provider account connected to Resgrid (plan B2.2). Stripe stores only the account
	/// id; the token ciphertext columns are reserved for v2 providers. System credentials: never cataloged for ADP,
	/// exported, searched or placed in a Workflow payload. Masked in every DTO.
	/// </summary>
	public class DepartmentPaymentConnection : IEntity
	{
		[Required]
		public string DepartmentPaymentConnectionId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary><see cref="PaymentProviders"/>.</summary>
		public int Provider { get; set; }

		/// <summary><see cref="PaymentConnectionStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary><see cref="PaymentEnvironments"/>.</summary>
		public int Environment { get; set; }

		/// <summary>The provider's account id (acct_…). Not a secret, but masked wherever it is shown.</summary>
		public string ExternalAccountId { get; set; }
		public string DisplayName { get; set; }
		public string Country { get; set; }
		public string DefaultCurrency { get; set; }

		/// <summary>Observed capabilities as a JSON object of name → active (card_payments, us_bank_account_ach_payments, charges_enabled).</summary>
		public string CapabilitiesJson { get; set; }
		public string ScopesCsv { get; set; }

		/// <summary>Reserved for v2 token-holding providers; null for Stripe.</summary>
		public string AccessTokenCiphertext { get; set; }
		public string RefreshTokenCiphertext { get; set; }
		public DateTime? TokenExpiresOn { get; set; }

		public DateTime ConnectedOn { get; set; }
		public string ConnectedByUserId { get; set; }
		public DateTime? DisconnectedOn { get; set; }
		public DateTime? LastVerifiedOn { get; set; }
		public string LastError { get; set; }
		public bool IsDefault { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped]
		public bool IsUsable => Status == (int)PaymentConnectionStatuses.Connected && !IsDeleted;

		/// <summary>acct_…1234: the last four characters only (plan B2.6).</summary>
		[NotMapped]
		public string MaskedExternalAccountId => Mask(ExternalAccountId);

		public static string Mask(string externalAccountId)
		{
			if (string.IsNullOrWhiteSpace(externalAccountId)) return null;
			var prefix = externalAccountId.IndexOf('_') > 0 ? externalAccountId.Substring(0, externalAccountId.IndexOf('_') + 1) : string.Empty;
			var tail = externalAccountId.Length > 4 ? externalAccountId.Substring(externalAccountId.Length - 4) : externalAccountId;
			return prefix + "…" + tail;
		}

		[NotMapped]
		public string TableName => "DepartmentPaymentConnections";

		[NotMapped]
		public string IdName => "DepartmentPaymentConnectionId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentPaymentConnectionId; }
			set { DepartmentPaymentConnectionId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsUsable", "MaskedExternalAccountId" };
	}

	/// <summary>One attempt to collect an invoice's remaining balance online (plan B2.2): a hosted Checkout Session.</summary>
	public class InvoicePaymentRequest : IEntity
	{
		[Required]
		public string InvoicePaymentRequestId { get; set; }

		[Required]
		public string InvoiceId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public string DepartmentPaymentConnectionId { get; set; }
		public int Provider { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }

		/// <summary><see cref="PaymentRequestStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>The provider's session id (cs_…).</summary>
		public string ExternalReference { get; set; }
		public string PaymentIntentId { get; set; }
		public string HostedUrl { get; set; }
		public DateTime ExpiresOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		public string InvoicePaymentId { get; set; }

		/// <summary><see cref="PaymentRequestSources"/>.</summary>
		public int Source { get; set; }
		public string CreatedByUserId { get; set; }
		public string LastError { get; set; }
		public DateTime AddedOn { get; set; }
		public DateTime UpdatedOn { get; set; }

		[NotMapped]
		public bool IsOpen => Status is (int)PaymentRequestStatuses.Created or (int)PaymentRequestStatuses.Opened or (int)PaymentRequestStatuses.Processing;

		public static bool IsTerminal(int status) => status is (int)PaymentRequestStatuses.Completed or (int)PaymentRequestStatuses.Expired or (int)PaymentRequestStatuses.Cancelled or (int)PaymentRequestStatuses.Failed;

		[NotMapped]
		public string TableName => "InvoicePaymentRequests";

		[NotMapped]
		public string IdName => "InvoicePaymentRequestId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return InvoicePaymentRequestId; }
			set { InvoicePaymentRequestId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsOpen" };
	}

	/// <summary>
	/// A received provider webhook event (plan B2.2): the idempotency ledger and the forensic copy of the body.
	/// The body contains no card data by construction (hosted Checkout); rows are purged after
	/// PaymentConnectConfig.EventRetentionDays. Its own table: the legacy SaaS-billing <see cref="PaymentProviderEvent"/>
	/// already owns PaymentProviderEvents with a different shape.
	/// </summary>
	public class PaymentConnectEvent : IEntity
	{
		[Required]
		public string PaymentConnectEventId { get; set; }

		public int Provider { get; set; }

		/// <summary>The provider's event id (evt_…); unique per provider.</summary>
		public string ExternalEventId { get; set; }
		public string ExternalAccountId { get; set; }
		public string EventType { get; set; }
		public int? DepartmentId { get; set; }
		public bool LiveMode { get; set; }
		public DateTime ReceivedOn { get; set; }
		public DateTime? ProcessedOn { get; set; }

		/// <summary><see cref="PaymentEventOutcomes"/>.</summary>
		public int Outcome { get; set; }
		public string Error { get; set; }
		public string PayloadJson { get; set; }

		[NotMapped]
		public string TableName => "PaymentConnectEvents";

		[NotMapped]
		public string IdName => "PaymentConnectEventId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PaymentConnectEventId; }
			set { PaymentConnectEventId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	// ---- provider seam DTOs (plan B2.3) -----------------------------------------------------------------------

	/// <summary>What a provider reports about a connected account.</summary>
	public class PaymentConnectionFacts
	{
		public string ExternalAccountId { get; set; }
		public string DisplayName { get; set; }
		public string Country { get; set; }
		public string DefaultCurrency { get; set; }
		public bool ChargesEnabled { get; set; }
		/// <summary>Capability name → active.</summary>
		public Dictionary<string, bool> Capabilities { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		public string ScopesCsv { get; set; }
		public bool LiveMode { get; set; }
		/// <summary>Reserved for v2 token-holding providers; null for Stripe.</summary>
		public string AccessToken { get; set; }
		public string RefreshToken { get; set; }
		public DateTime? TokenExpiresOn { get; set; }
	}

	/// <summary>Everything the provider needs to open a hosted payment page for one invoice balance.</summary>
	public class PaymentRequestSpec
	{
		public string ExternalAccountId { get; set; }
		public string PaymentRequestId { get; set; }
		public int DepartmentId { get; set; }
		public string InvoiceId { get; set; }
		public int InvoiceNumber { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }
		public string CustomerEmail { get; set; }
		public IReadOnlyList<string> PaymentMethodTypes { get; set; }
		public string SuccessUrl { get; set; }
		public string CancelUrl { get; set; }
		public DateTime ExpiresOn { get; set; }
		public string IdempotencyKey { get; set; }
	}

	/// <summary>The provider's answer to <see cref="PaymentRequestSpec"/>.</summary>
	public class PaymentRequestCreation
	{
		public string ExternalReference { get; set; }
		public string PaymentIntentId { get; set; }
		public string HostedUrl { get; set; }
		public DateTime ExpiresOn { get; set; }
	}

	/// <summary>Current state of a hosted payment request, read back from the provider (reconciliation).</summary>
	public class PaymentRequestState
	{
		/// <summary>open, complete, expired.</summary>
		public string SessionStatus { get; set; }
		/// <summary>paid, unpaid, no_payment_required.</summary>
		public string PaymentStatus { get; set; }
		public string PaymentIntentId { get; set; }
		public PaymentChargeFacts Charge { get; set; }
	}

	/// <summary>Settled-charge facts for the payment row: fee/net from the balance transaction, method summary, receipt.</summary>
	public class PaymentChargeFacts
	{
		public string ChargeId { get; set; }
		public string PaymentIntentId { get; set; }
		public decimal Amount { get; set; }
		public string Currency { get; set; }
		public decimal? Fee { get; set; }
		public decimal? Net { get; set; }
		public string PayerEmail { get; set; }
		public string MethodSummary { get; set; }
		public string ReceiptUrl { get; set; }
		public DateTime? PaidOn { get; set; }
	}

	/// <summary>Provider-neutral webhook event (plan B2.3).</summary>
	public class PaymentProviderEventEnvelope
	{
		public int Provider { get; set; }
		public string ExternalEventId { get; set; }
		public string ExternalAccountId { get; set; }
		public string EventType { get; set; }
		public PaymentEventKinds Kind { get; set; }
		public bool LiveMode { get; set; }
		public DateTime OccurredOn { get; set; }
		/// <summary>Session id (cs_…) when the event carries one.</summary>
		public string ExternalReference { get; set; }
		public string PaymentIntentId { get; set; }
		public string ChargeId { get; set; }
		public decimal? Amount { get; set; }
		public string Currency { get; set; }
		public decimal? RefundedAmount { get; set; }
		public string PayerEmail { get; set; }
		public string MethodSummary { get; set; }
		public string ReceiptUrl { get; set; }
		/// <summary>DisputeClosed: true when the dispute was lost.</summary>
		public bool? DisputeLost { get; set; }
		/// <summary>ConnectionUpdated: charges_enabled and capabilities as observed.</summary>
		public bool? ChargesEnabled { get; set; }
		public Dictionary<string, bool> Capabilities { get; set; }
	}

	/// <summary>Result of receiving one webhook body.</summary>
	public class PaymentWebhookReceipt
	{
		/// <summary>True when the receiver answered 2xx (applied, ignored or duplicate).</summary>
		public bool Accepted { get; set; }
		public PaymentEventOutcomes Outcome { get; set; }
		public string Error { get; set; }
	}

	/// <summary>The Connect OAuth hand-off (plan B2.4).</summary>
	public class PaymentConnectBegin
	{
		public string AuthorizeUrl { get; set; }
		public string State { get; set; }
		public DateTime ExpiresOn { get; set; }
	}

	/// <summary>What the anonymous pay page shows (plan B2.5): number, amount, due date, department name. Nothing else.</summary>
	public class PayPageModel
	{
		public bool Available { get; set; }
		/// <summary>Resource key explaining why the page is unavailable (expired link, paid, not offered).</summary>
		public string UnavailableReason { get; set; }
		public string InvoiceId { get; set; }
		public int DepartmentId { get; set; }
		public int InvoiceNumber { get; set; }
		public string DepartmentName { get; set; }
		public decimal AmountDue { get; set; }
		public string Currency { get; set; }
		public DateTime? DueOn { get; set; }
		public bool Paid { get; set; }
		/// <summary>An open request whose hosted page can be resumed.</summary>
		public string OpenHostedUrl { get; set; }
		public bool Processing { get; set; }
	}

	/// <summary>The department's online-payments posture for the settings page and the API (plan B2.5).</summary>
	public class OnlinePaymentsStatus
	{
		/// <summary>PaymentConnectConfig.Enabled and the Payments.StripeConnect cluster flag.</summary>
		public bool AvailableInCluster { get; set; }
		/// <summary>Invoicing.OnlinePayments evaluates true for the department.</summary>
		public bool FlagEnabled { get; set; }
		/// <summary>DepartmentBillingIdentities.OnlinePaymentsEnabled.</summary>
		public bool EnabledByDepartment { get; set; }
		public bool ShowPayOnlineOnDocuments { get; set; }
		public int PayLinkExpiryDays { get; set; }
		public IReadOnlyList<string> AllowedPaymentMethods { get; set; } = new List<string>();
		/// <summary>The default connection, masked; null when none.</summary>
		public DepartmentPaymentConnection Connection { get; set; }
		public Dictionary<string, bool> Capabilities { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		/// <summary>All gates hold: a pay link can be created right now.</summary>
		public bool CanCollect { get; set; }
		/// <summary>The gate that fails first, as a resource key (payments_*), or null.</summary>
		public string BlockedReason { get; set; }
	}

	/// <summary>
	/// The Phase B columns Advanced Data Protection catalogs (ADP catalog 26, plan B2.2): customer-contact and
	/// payment-reference text under the Contacts family. Amounts, statuses, numbers and dates stay metadata.
	/// </summary>
	public static class InvoicingProtectedFields
	{
		public const int CatalogVersion = 26;
		public const string Family = "Contacts";

		public static readonly IReadOnlyDictionary<string, (Func<CustomerBillingProfile, string> Get, Action<CustomerBillingProfile, string> Set)> BillingProfile =
			new Dictionary<string, (Func<CustomerBillingProfile, string>, Action<CustomerBillingProfile, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["customerbillingprofiles.billingemail"] = (p => p.BillingEmail, (p, v) => p.BillingEmail = v)
			};

		public static readonly IReadOnlyDictionary<string, (Func<Invoice, string> Get, Action<Invoice, string> Set)> Invoice =
			new Dictionary<string, (Func<Invoice, string>, Action<Invoice, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["invoices.senttoemail"] = (i => i.SentToEmail, (i, v) => i.SentToEmail = v),
				["invoices.notes"] = (i => i.Notes, (i, v) => i.Notes = v)
			};

		public static readonly IReadOnlyDictionary<string, (Func<InvoicePayment, string> Get, Action<InvoicePayment, string> Set)> Payment =
			new Dictionary<string, (Func<InvoicePayment, string>, Action<InvoicePayment, string>)>(StringComparer.OrdinalIgnoreCase)
			{
				["invoicepayments.payeremail"] = (p => p.PayerEmail, (p, v) => p.PayerEmail = v),
				["invoicepayments.reference"] = (p => p.Reference, (p, v) => p.Reference = v),
				["invoicepayments.receipturl"] = (p => p.ReceiptUrl, (p, v) => p.ReceiptUrl = v),
				["invoicepayments.notes"] = (p => p.Notes, (p, v) => p.Notes = v)
			};

		/// <summary>Every cataloged field id, for tests and the catalog builder.</summary>
		public static IEnumerable<(string Table, string Column)> All()
		{
			yield return ("CustomerBillingProfiles", "BillingEmail");
			yield return ("Invoices", "SentToEmail");
			yield return ("Invoices", "Notes");
			yield return ("InvoicePayments", "PayerEmail");
			yield return ("InvoicePayments", "Reference");
			yield return ("InvoicePayments", "ReceiptUrl");
			yield return ("InvoicePayments", "Notes");
		}
	}
}
