using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Invoicing
{
	/// <summary>
	/// The department's own billing identity that prints on every invoice and bid (plan B1): legal name, remit-to
	/// address, tax registrations and government registrations, plus the Phase B2 online-payment settings. One row
	/// per department, keyed by DepartmentId; written through the repository's upsert.
	/// </summary>
	public class DepartmentBillingIdentity : IEntity
	{
		[Required]
		public int DepartmentId { get; set; }
		public string LegalBusinessName { get; set; }
		public int? RemitToAddressId { get; set; }

		/// <summary>GST/HST number or EIN/TIN. Printed on invoices (CRA requirement for GST/HST).</summary>
		public string TaxRegistrationNumber { get; set; }

		/// <summary>Provincial sales tax registration where applicable.</summary>
		public string SecondaryTaxRegistrationNumber { get; set; }
		public string SamUei { get; set; }
		public string CageCode { get; set; }
		public string WorkersCompAccountNumber { get; set; }
		public string InvoiceFooterText { get; set; }

		// Phase B2 online payments
		public bool OnlinePaymentsEnabled { get; set; }
		public string DefaultPaymentConnectionId { get; set; }

		/// <summary>Comma-separated Stripe payment method types offered on Checkout ("card,us_bank_account").</summary>
		public string AllowedPaymentMethodsCsv { get; set; }
		public int PayLinkExpiryDays { get; set; } = 30;
		public bool ShowPayOnlineOnDocuments { get; set; } = true;
		public DateTime UpdatedOn { get; set; }
		public string UpdatedByUserId { get; set; }
		/// <summary>Reserved marker columns (never set): the registrations print on every invoice, so they are not under ADP.</summary>
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }

		[NotMapped]
		public string TableName => "DepartmentBillingIdentities";

		[NotMapped]
		public string IdName => "DepartmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentId; }
			set { DepartmentId = (int)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
