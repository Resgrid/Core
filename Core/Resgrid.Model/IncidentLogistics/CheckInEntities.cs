using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0292 (both dialects).

	/// <summary>A back-office check-in record (plan §9.5): who arrived, what they brought and what they need. Operational accountability stays IC's.</summary>
	public class IncidentCheckIn : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentCheckInId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentSubjectTypes"/>.</summary>
		public int SubjectType { get; set; }
		public string SubjectId { get; set; }
		/// <summary><see cref="IncidentCheckInSources"/>.</summary>
		public int Source { get; set; }
		public DateTime CheckedInOn { get; set; }
		public string MobilizationOrigin { get; set; }
		public int? ActualPersonnelCount { get; set; }
		public string VehicleDescription { get; set; }
		public string ReportingPoint { get; set; }
		public DateTime? FirstWorkDay { get; set; }
		public int? AssignmentLengthDays { get; set; }
		public string SpecialRequirements { get; set; }
		public bool IsDuplicateCandidate { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentCheckIns";
		[NotMapped] public string IdName => "IncidentCheckInId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentCheckInId; set => IncidentCheckInId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>The release plan of a resource or participant (plan §9.5): tentative, approved, released, departed. The checklist under it is the gate.</summary>
	public class IncidentDemobPlan : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentDemobPlanId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentSubjectTypes"/>.</summary>
		public int SubjectType { get; set; }
		public string SubjectId { get; set; }
		/// <summary><see cref="IncidentDemobStatuses"/>.</summary>
		public int Status { get; set; }
		public DateTime? TentativeReleaseOn { get; set; }
		public DateTime? ApprovedReleaseOn { get; set; }
		public string ApprovedByUserId { get; set; }
		public DateTime? DepartureWindowStart { get; set; }
		public DateTime? DepartureWindowEnd { get; set; }
		public string Destination { get; set; }
		public string TravelMode { get; set; }
		public bool ReassignmentEligible { get; set; }
		public DateTime? DepartedOn { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentDemobPlans";
		[NotMapped] public string IdName => "IncidentDemobPlanId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentDemobPlanId; set => IncidentDemobPlanId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One demob gate item with owner, blocking flag and an authorized-waiver path (plan §9.5).</summary>
	public class IncidentDemobChecklistItem : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentDemobChecklistItemId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentDemobPlanId { get; set; }
		/// <summary><see cref="IncidentDemobCategories"/>.</summary>
		public int Category { get; set; }
		public string Name { get; set; }
		public string OwnerPosition { get; set; }
		public bool IsBlocking { get; set; }
		/// <summary><see cref="IncidentGateItemStatuses"/>.</summary>
		public int Status { get; set; }
		public DateTime? CompletedOn { get; set; }
		public string CompletedByUserId { get; set; }
		public string WaivedReason { get; set; }
		public string WaivedByUserId { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentDemobChecklistItems";
		[NotMapped] public string IdName => "IncidentDemobChecklistItemId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentDemobChecklistItemId; set => IncidentDemobChecklistItemId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
