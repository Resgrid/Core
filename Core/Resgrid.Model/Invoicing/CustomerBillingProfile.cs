using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.Invoicing
{
	/// <summary>
	/// Makes a Contact billable (plan decision 4): a separate row referencing the Contact, so billing is optional per
	/// contact and dispatch paths never depend on it. One live row per contact.
	/// </summary>
	public class CustomerBillingProfile : IEntity
	{
		[Required]
		public string CustomerBillingProfileId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public string ContactId { get; set; }

		/// <summary>ADP catalog 26 field (Phase B2) in an enrolled department.</summary>
		public string BillingEmail { get; set; }
		public int? BillingAddressId { get; set; }
		public bool UseContactMailingAddress { get; set; } = true;
		public int TermsNetDays { get; set; } = 30;
		public bool TaxExempt { get; set; }

		/// <summary>Flat tax rate as a percentage (7.25 = 7.25%). Ignored when <see cref="TaxComponentsJson"/> is present.</summary>
		public decimal? TaxRate { get; set; }

		/// <summary>Named multi-component taxes (plan decision 23): JSON array of {name, ratePercent, registrationNumber}.</summary>
		public string TaxComponentsJson { get; set; }
		public string DefaultRateCardId { get; set; }

		/// <summary>Contact-level discount applied to every new invoice (plan decision 14), as a percentage.</summary>
		public decimal? DefaultDiscountPercent { get; set; }

		/// <summary>Reserved for Phase C contractor billing.</summary>
		public string DefaultRateScheduleId { get; set; }
		public bool PurchaseOrderRequired { get; set; }
		public string Notes { get; set; }
		public bool Active { get; set; } = true;
		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }

		[NotMapped]
		public string TableName => "CustomerBillingProfiles";

		[NotMapped]
		public string IdName => "CustomerBillingProfileId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CustomerBillingProfileId; }
			set { CustomerBillingProfileId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One named tax component on a billing profile or, snapshotted with its amount, on an invoice (decision 23).</summary>
	public class TaxComponent
	{
		public string Name { get; set; }
		public decimal RatePercent { get; set; }
		public string RegistrationNumber { get; set; }

		/// <summary>Set only on the invoice snapshot: the amount this component contributed.</summary>
		public decimal? Amount { get; set; }
	}
}
