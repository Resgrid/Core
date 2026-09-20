using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Invoicing
{
	/// <summary>
	/// A customer invoice (plan decisions 6–8, 12, 14, 23). Totals are recomputed server-side only; the number is
	/// assigned from InvoiceNumberSequences at draft creation. Phase C contractor invoices and Phase B2 online
	/// payments land on this same record.
	/// </summary>
	public class Invoice : IEntity
	{
		[Required]
		public string InvoiceId { get; set; }

		[Required]
		public int DepartmentId { get; set; }
		public int InvoiceNumber { get; set; }

		[Required]
		public string CustomerBillingProfileId { get; set; }

		[Required]
		public string ContactId { get; set; }

		/// <summary><see cref="InvoiceStatus"/>.</summary>
		public int Status { get; set; }
		public DateTime? IssuedOn { get; set; }
		public DateTime? DueOn { get; set; }
		public string Currency { get; set; } = "USD";
		public decimal SubTotal { get; set; }
		public decimal? DiscountPercent { get; set; }
		public decimal DiscountAmount { get; set; }
		public decimal TaxAmount { get; set; }
		public decimal Total { get; set; }
		public decimal AmountPaid { get; set; }

		/// <summary>Snapshot of the applied tax components with per-component amounts (decision 23); null when a flat rate applied.</summary>
		public string TaxComponentsJson { get; set; }

		/// <summary>Customer-facing: not under ADP (the customer reads it without a login).</summary>
		public string Notes { get; set; }
		public string TermsText { get; set; }
		public DateTime? SentOn { get; set; }

		/// <summary>Customer-facing: not under ADP (the customer reads it without a login).</summary>
		public string SentToEmail { get; set; }
		public DateTime? PaidOn { get; set; }
		public DateTime? VoidedOn { get; set; }
		public string VoidReason { get; set; }

		/// <summary>Reserved (Phase B2): always null in v1, Resgrid takes no fee.</summary>
		public decimal? PlatformFeeAmount { get; set; }

		// Phase C provenance (M0219): the contract and deployment a contractor invoice was generated from.
		public string ServiceContractId { get; set; }
		public string DeploymentId { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped]
		public List<InvoiceLineItem> LineItems { get; set; }

		[NotMapped]
		public List<InvoicePayment> Payments { get; set; }

		[NotMapped]
		public decimal Balance => Total - AmountPaid;

		[NotMapped]
		public string TableName => "Invoices";

		[NotMapped]
		public string IdName => "InvoiceId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return InvoiceId; }
			set { InvoiceId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "LineItems", "Payments", "Balance" };
	}
}
