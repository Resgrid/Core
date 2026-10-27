using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0293 (both dialects).

	/// <summary>A support-service template (plan §9.10): radio, print/copy, IT, sanitation and custom services. Template-specific fields are UDF definitions, never columns.</summary>
	public class IncidentServiceTemplate : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentServiceTemplateId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		/// <summary><see cref="IncidentServiceCategories"/>.</summary>
		public int Category { get; set; }
		public int? DefaultSlaMinutes { get; set; }
		public bool IsSelfService { get; set; }
		public bool IsActive { get; set; } = true;
		public string Description { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentServiceTemplates";
		[NotMapped] public string IdName => "IncidentServiceTemplateId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentServiceTemplateId; set => IncidentServiceTemplateId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A support-service ticket (plan §9.10) with SLA, assignment, vendor, cost and confirmation.</summary>
	public class IncidentServiceRequest : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string IncidentServiceRequestId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentOperationalPeriodId { get; set; }
		public string IncidentServiceTemplateId { get; set; }
		public string TicketNumber { get; set; }
		public string RequesterUserId { get; set; }
		public string RequesterParticipantId { get; set; }
		public string IncidentFacilityId { get; set; }
		public string Location { get; set; }
		/// <summary><see cref="IncidentRequestPriorities"/>.</summary>
		public int Priority { get; set; }
		public DateTime? NeededBy { get; set; }
		public string Title { get; set; }
		public decimal? Quantity { get; set; }
		public string Unit { get; set; }
		public string AssetReference { get; set; }
		public string AssignedPosition { get; set; }
		public string AssignedUserId { get; set; }
		public string IncidentVendorEngagementId { get; set; }
		/// <summary><see cref="IncidentServiceRequestStatuses"/>.</summary>
		public int Status { get; set; }
		public DateTime? SlaDueOn { get; set; }
		public bool SlaBreached { get; set; }
		public DateTime? CompletedOn { get; set; }
		public DateTime? CustomerConfirmedOn { get; set; }
		public decimal? CostAmount { get; set; }
		public string Currency { get; set; }
		public string WorkOrderId { get; set; }
		/// <summary>ADP-protected (Sensitive, Operational family).</summary>
		public string Description { get; set; }
		/// <summary>ADP-protected (Sensitive, Operational family).</summary>
		public string CompletionNotes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentServiceRequests";
		[NotMapped] public string IdName => "IncidentServiceRequestId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentServiceRequestId; set => IncidentServiceRequestId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A note, labor, parts or cost entry on a service ticket.</summary>
	public class IncidentServiceActivity : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentProtectable
	{
		[Required]
		public string IncidentServiceActivityId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentServiceRequestId { get; set; }
		/// <summary><see cref="IncidentServiceActivityKinds"/>.</summary>
		public int Kind { get; set; }
		public int? LaborMinutes { get; set; }
		public decimal? Amount { get; set; }
		public string Currency { get; set; }
		public string InventoryTransactionId { get; set; }
		/// <summary>ADP-protected (Sensitive, Operational family).</summary>
		public string Description { get; set; }
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentServiceActivities";
		[NotMapped] public string IdName => "IncidentServiceActivityId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentServiceActivityId; set => IncidentServiceActivityId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A copier, generator, shower unit, network device or similar asset a service queue works (plan §9.10).</summary>
	public class IncidentServiceAsset : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentServiceAssetId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentServiceTemplateId { get; set; }
		public string Name { get; set; }
		/// <summary><see cref="IncidentServiceAssetKinds"/>.</summary>
		public int Kind { get; set; }
		public string SerialNumber { get; set; }
		public string InventoryAssetId { get; set; }
		public string IncidentFacilityId { get; set; }
		/// <summary><see cref="IncidentServiceReadiness"/>.</summary>
		public int Status { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentServiceAssets";
		[NotMapped] public string IdName => "IncidentServiceAssetId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentServiceAssetId; set => IncidentServiceAssetId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
