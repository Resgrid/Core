using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0294 (both dialects).

	/// <summary>A department's pre-incident local support plan (plan §9.12.1).</summary>
	public class DepartmentEmergencySupportPlan : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string DepartmentEmergencySupportPlanId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public DateTime? ReviewedOn { get; set; }
		public DateTime? NextReviewOn { get; set; }
		/// <summary><see cref="IncidentCatalogStatuses"/>.</summary>
		public int Status { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "DepartmentEmergencySupportPlans";
		[NotMapped] public string IdName => "DepartmentEmergencySupportPlanId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => DepartmentEmergencySupportPlanId; set => DepartmentEmergencySupportPlanId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A geography or function area of a support plan.</summary>
	public class SupportPlanArea : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string SupportPlanAreaId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string DepartmentEmergencySupportPlanId { get; set; }
		public string Name { get; set; }
		public string Geography { get; set; }
		public string IncidentTypes { get; set; }
		public string ActivationThreshold { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "SupportPlanAreas";
		[NotMapped] public string IdName => "SupportPlanAreaId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => SupportPlanAreaId; set => SupportPlanAreaId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Primary, alternate and contingency vendors per category in an area (plan §9.12.1).</summary>
	public class SupportPlanVendorPreference : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string SupportPlanVendorPreferenceId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string DepartmentEmergencySupportPlanId { get; set; }
		public string SupportPlanAreaId { get; set; }
		public string VendorId { get; set; }
		/// <summary><see cref="IncidentSupportCategories"/>.</summary>
		public int Category { get; set; }
		/// <summary><see cref="IncidentVendorRanks"/>.</summary>
		public int Rank { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "SupportPlanVendorPreferences";
		[NotMapped] public string IdName => "SupportPlanVendorPreferenceId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => SupportPlanVendorPreferenceId; set => SupportPlanVendorPreferenceId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>The support plan as it stood when an activation took it (plan §1 commitment 8).</summary>
	public class IncidentSupportPlanSnapshot : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentSupportPlanSnapshotId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string DepartmentEmergencySupportPlanId { get; set; }
		public string ContentJson { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentSupportPlanSnapshots";
		[NotMapped] public string IdName => "IncidentSupportPlanSnapshotId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentSupportPlanSnapshotId; set => IncidentSupportPlanSnapshotId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A branch or service location of a vendor (plan §9.12.1). Extends the shipped InventoryVendors row by VendorId.</summary>
	public class VendorLocation : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string VendorLocationId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string VendorId { get; set; }
		public string Name { get; set; }
		public string Address { get; set; }
		public decimal? ServiceRadiusKm { get; set; }
		public string DispatchPhone { get; set; }
		public string AfterHoursPhone { get; set; }
		public string OrderingChannel { get; set; }
		public string Hours { get; set; }
		public bool Is24x7 { get; set; }
		public string Languages { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "VendorLocations";
		[NotMapped] public string IdName => "VendorLocationId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => VendorLocationId; set => VendorLocationId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A typed vendor capability with units and normal/maximum capacity (plan §9.12.1). Capacity commitments lock this row.</summary>
	public class VendorCapability : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string VendorCapabilityId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string VendorId { get; set; }
		public string VendorLocationId { get; set; }
		/// <summary><see cref="IncidentSupportCategories"/>.</summary>
		public int Category { get; set; }
		public string Name { get; set; }
		public string Unit { get; set; }
		public decimal NormalCapacity { get; set; }
		public decimal MaxCapacity { get; set; }
		public int? LeadTimeHours { get; set; }
		public string MinimumOrder { get; set; }
		public string CutoffTime { get; set; }
		/// <summary><see cref="IncidentCatalogStatuses"/>.</summary>
		public int Status { get; set; }
		public DateTime? LastVerifiedOn { get; set; }
		public string VerifiedByUserId { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "VendorCapabilities";
		[NotMapped] public string IdName => "VendorCapabilityId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => VendorCapabilityId; set => VendorCapabilityId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A dated availability or blackout window of a capability.</summary>
	public class VendorAvailabilityWindow : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string VendorAvailabilityWindowId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string VendorCapabilityId { get; set; }
		public DateTime StartsOn { get; set; }
		public DateTime EndsOn { get; set; }
		/// <summary><see cref="IncidentAvailabilityKinds"/>.</summary>
		public int Kind { get; set; }
		public decimal? Capacity { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "VendorAvailabilityWindows";
		[NotMapped] public string IdName => "VendorAvailabilityWindowId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => VendorAvailabilityWindowId; set => VendorAvailabilityWindowId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Insurance, licence, safety and tax-form metadata with expiry (plan §9.12.1).</summary>
	public class VendorComplianceDocument : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string VendorComplianceDocumentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string VendorId { get; set; }
		/// <summary><see cref="IncidentComplianceDocumentKinds"/>.</summary>
		public int Kind { get; set; }
		public string DocumentNumber { get; set; }
		public DateTime? IssuedOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		/// <summary><see cref="IncidentCatalogStatuses"/>.</summary>
		public int Status { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "VendorComplianceDocuments";
		[NotMapped] public string IdName => "VendorComplianceDocumentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => VendorComplianceDocumentId; set => VendorComplianceDocumentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A vendor's tax identifier and remittance detail (plan §15.3a): a sole proprietor's TIN can be an SSN.</summary>
	public class VendorProtectedDetail : IEntity, IIncidentLedgerEntity, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string VendorProtectedDetailId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string VendorId { get; set; }
		/// <summary>ADP-protected (Pii, Contacts family).</summary>
		public string TaxIdentifier { get; set; }
		/// <summary>ADP-protected (Pii, Contacts family).</summary>
		public string RemittanceDetails { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "VendorProtectedDetails";
		[NotMapped] public string IdName => "VendorProtectedDetailId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => VendorProtectedDetailId; set => VendorProtectedDetailId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An incident's time-bounded engagement of a vendor (plan §9.12.3), distinct from the vendor's standing eligibility.</summary>
	public class IncidentVendorEngagement : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentVendorEngagementId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string VendorId { get; set; }
		public string VendorCapabilityId { get; set; }
		public string VendorAgreementId { get; set; }
		public string ServiceDescription { get; set; }
		public string IncidentFacilityId { get; set; }
		/// <summary><see cref="IncidentEngagementStatuses"/>.</summary>
		public int Status { get; set; }
		public string PurchaserUserId { get; set; }
		public string OwnerUserId { get; set; }
		public DateTime? WindowStart { get; set; }
		public DateTime? WindowEnd { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentVendorEngagements";
		[NotMapped] public string IdName => "IncidentVendorEngagementId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentVendorEngagementId; set => IncidentVendorEngagementId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A time-bucketed hold or commitment of vendor capacity (plan §11.5), checked across concurrent incidents in a locked transaction.</summary>
	public class IncidentVendorCapacityCommitment : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentVendorCapacityCommitmentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentVendorEngagementId { get; set; }
		public string VendorCapabilityId { get; set; }
		public DateTime BucketStart { get; set; }
		public DateTime BucketEnd { get; set; }
		/// <summary><see cref="IncidentCommitmentKinds"/>.</summary>
		public int Kind { get; set; }
		public decimal Quantity { get; set; }
		public string Unit { get; set; }
		/// <summary><see cref="IncidentHoldStatuses"/>.</summary>
		public int Status { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public bool IsOverride { get; set; }
		public string OverrideReason { get; set; }
		public string ApprovedByUserId { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentVendorCapacityCommitments";
		[NotMapped] public string IdName => "IncidentVendorCapacityCommitmentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentVendorCapacityCommitmentId; set => IncidentVendorCapacityCommitmentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A quote requested from or returned by an engaged vendor.</summary>
	public class IncidentVendorQuote : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentVendorQuoteId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentVendorEngagementId { get; set; }
		public DateTime RequestedOn { get; set; }
		public DateTime? ReceivedOn { get; set; }
		public decimal? Amount { get; set; }
		public string Currency { get; set; }
		public string Terms { get; set; }
		/// <summary><see cref="IncidentQuoteStatuses"/>.</summary>
		public int Status { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentVendorQuotes";
		[NotMapped] public string IdName => "IncidentVendorQuoteId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentVendorQuoteId; set => IncidentVendorQuoteId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
