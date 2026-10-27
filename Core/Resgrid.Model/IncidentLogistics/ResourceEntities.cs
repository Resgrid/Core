using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0292 (both dialects).

	/// <summary>A supported resource on the incident (plan §9.3): crew, team, equipment, aircraft, supply or service, with provider, ordering authority, order numbers and counts. Support status is owned here; tactical assignment stays IC's (CurrentResourceAssignmentId is a display-only projection).</summary>
	public class IncidentResource : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentResourceId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentResourceKinds"/>.</summary>
		public int Kind { get; set; }
		public string Name { get; set; }
		public string ResourceType { get; set; }
		public string Capability { get; set; }
		public string TypingCode { get; set; }
		public string ProviderAgency { get; set; }
		public string PayingAgency { get; set; }
		public string OrderingAuthority { get; set; }
		public string OrderNumber { get; set; }
		public string RequestNumber { get; set; }
		public string HomeLocation { get; set; }
		public string LeaderName { get; set; }
		public string LeaderParticipantId { get; set; }
		public int PersonnelCount { get; set; }
		public int ApparatusCount { get; set; }
		public int? UnitId { get; set; }
		public string IncidentAdHocUnitId { get; set; }
		public string IncidentRequestFillId { get; set; }
		public string IncidentMissionPackageId { get; set; }
		public string IncidentVendorEngagementId { get; set; }
		/// <summary><see cref="IncidentSupportStatuses"/>.</summary>
		public int SupportStatus { get; set; }
		public string CurrentResourceAssignmentId { get; set; }
		/// <summary><see cref="IncidentPaymentClasses"/>.</summary>
		public int PaymentClass { get; set; }
		public DateTime? ArrivedOn { get; set; }
		public DateTime? CheckedInOn { get; set; }
		public DateTime? AssignmentStartOn { get; set; }
		public int? MaxAssignmentDays { get; set; }
		public DateTime? TimeoutOn { get; set; }
		/// <summary><see cref="IncidentTimeoutBases"/>.</summary>
		public int TimeoutBasis { get; set; }
		public DateTime? TentativeReleaseOn { get; set; }
		public DateTime? ReleasedOn { get; set; }
		public DateTime? DepartedOn { get; set; }
		public string Destination { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentResources";
		[NotMapped] public string IdName => "IncidentResourceId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentResourceId; set => IncidentResourceId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A component of a composite resource (strike team, task force, mission package).</summary>
	public class IncidentResourceComponent : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentResourceComponentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentResourceId { get; set; }
		public string ComponentResourceId { get; set; }
		public string IncidentParticipantId { get; set; }
		/// <summary><see cref="IncidentResourceKinds"/>.</summary>
		public int Kind { get; set; }
		public string Name { get; set; }
		public decimal Quantity { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentResourceComponents";
		[NotMapped] public string IdName => "IncidentResourceComponentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentResourceComponentId; set => IncidentResourceComponentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Append-only support-status history of a resource or participant (plan §9.5).</summary>
	public class IncidentResourceStatusHistory : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentResourceStatusHistoryId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentSubjectTypes"/>.</summary>
		public int SubjectType { get; set; }
		public string SubjectId { get; set; }
		public int OldStatus { get; set; }
		public int NewStatus { get; set; }
		public string Reason { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentResourceStatusHistory";
		[NotMapped] public string IdName => "IncidentResourceStatusHistoryId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentResourceStatusHistoryId; set => IncidentResourceStatusHistoryId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A mobilization, reassignment, transfer or demob movement of a resource (plan §9.5).</summary>
	public class IncidentResourceMovement : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentResourceMovementId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentResourceId { get; set; }
		/// <summary><see cref="IncidentMovementKinds"/>.</summary>
		public int Kind { get; set; }
		public string FromLocation { get; set; }
		public string ToLocation { get; set; }
		public DateTime? DepartedOn { get; set; }
		public DateTime? ArrivedOn { get; set; }
		public DateTime? EtaOn { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentResourceMovements";
		[NotMapped] public string IdName => "IncidentResourceMovementId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentResourceMovementId; set => IncidentResourceMovementId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An authorized maximum-time-allowance extension (plan §9.5): the prior projection is preserved, the predictive board shows what moved and why.</summary>
	public class IncidentAssignmentExtension : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentAssignmentExtensionId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentSubjectTypes"/>.</summary>
		public int SubjectType { get; set; }
		public string SubjectId { get; set; }
		public DateTime? PriorTimeoutOn { get; set; }
		public DateTime NewTimeoutOn { get; set; }
		public string ApprovedByUserId { get; set; }
		public string Reason { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentAssignmentExtensions";
		[NotMapped] public string IdName => "IncidentAssignmentExtensionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentAssignmentExtensionId; set => IncidentAssignmentExtensionId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A reusable mission package template (department scope, IsTemplate) or its incident snapshot taken at mobilization (plan §9.3).</summary>
	public class IncidentMissionPackage : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentMissionPackageId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		/// <summary><see cref="IncidentResourceKinds"/>.</summary>
		public int Kind { get; set; }
		public string Description { get; set; }
		public string ComponentsJson { get; set; }
		public bool IsTemplate { get; set; }
		public string SourceTemplateId { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentMissionPackages";
		[NotMapped] public string IdName => "IncidentMissionPackageId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentMissionPackageId; set => IncidentMissionPackageId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An estimate line of a mission package (personnel, equipment, travel, lodging, meals, commodities...).</summary>
	public class IncidentMissionPackageEstimate : IEntity, IIncidentLedgerEntity, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentMissionPackageEstimateId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentMissionPackageId { get; set; }
		/// <summary><see cref="IncidentEstimateCategories"/>.</summary>
		public int Category { get; set; }
		public decimal Quantity { get; set; }
		public string Unit { get; set; }
		public decimal? Amount { get; set; }
		public string Currency { get; set; }
		public string Notes { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentMissionPackageEstimates";
		[NotMapped] public string IdName => "IncidentMissionPackageEstimateId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentMissionPackageEstimateId; set => IncidentMissionPackageEstimateId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
