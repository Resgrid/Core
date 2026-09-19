using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Invoicing
{
	/// <summary>
	/// A payment against an invoice. Manual records (plan decision 10) and Phase B2 online payments share this row;
	/// online rows carry the provider id, fee/net, payer e-mail and a display-only method summary, never a PAN.
	/// </summary>
	public class InvoicePayment : IEntity
	{
		[Required]
		public string InvoicePaymentId { get; set; }

		[Required]
		public string InvoiceId { get; set; }

		[Required]
		public int DepartmentId { get; set; }
		public decimal Amount { get; set; }

		/// <summary><see cref="InvoicePaymentMethods"/>.</summary>
		public int Method { get; set; }

		/// <summary>Check number, remittance reference. ADP catalog 26 field (Phase B2).</summary>
		public string Reference { get; set; }

		/// <summary>Provider payment / charge / capture id (Phase B2); null for manual records.</summary>
		public string GatewayTransactionId { get; set; }
		public string PaymentRequestId { get; set; }

		/// <summary><see cref="PaymentProviders"/>; null for manual records.</summary>
		public int? Provider { get; set; }

		/// <summary><see cref="InvoicePaymentStatuses"/>.</summary>
		public int Status { get; set; }
		public decimal RefundedAmount { get; set; }
		public decimal? ProviderFeeAmount { get; set; }
		public decimal? NetAmount { get; set; }

		/// <summary>ADP catalog 26 field (Phase B2).</summary>
		public string PayerEmail { get; set; }

		/// <summary>"Visa •••• 4242", "ACH" — display only.</summary>
		public string PaymentMethodSummary { get; set; }

		/// <summary>ADP catalog 26 field (Phase B2).</summary>
		public string ReceiptUrl { get; set; }

		/// <summary>ADP catalog 26 field (Phase B2).</summary>
		public string Notes { get; set; }
		public DateTime PaidOn { get; set; }

		/// <summary>Null when a provider webhook recorded the payment.</summary>
		public string RecordedByUserId { get; set; }
		public DateTime AddedOn { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }

		/// <summary>The amount still counting toward the invoice after refunds and lost disputes.</summary>
		[NotMapped]
		public decimal EffectiveAmount => Status == (int)InvoicePaymentStatuses.DisputeLost ? 0 : Amount - RefundedAmount;

		[NotMapped]
		public string TableName => "InvoicePayments";

		[NotMapped]
		public string IdName => "InvoicePaymentId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return InvoicePaymentId; }
			set { InvoicePaymentId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "EffectiveAmount" };
	}
}
