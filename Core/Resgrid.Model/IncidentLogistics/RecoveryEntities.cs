using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0296 (both dialects).

	/// <summary>A FEMA Public Assistance or state program declaration (plan §9.26). Resgrid never determines eligibility or computes a payable federal share.</summary>
	public class IncidentDisasterDeclaration : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentDisasterDeclarationId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string DeclarationNumber { get; set; }
		/// <summary><see cref="IncidentRecoveryPrograms"/>.</summary>
		public int Program { get; set; }
		public string State { get; set; }
		public DateTime? IncidentPeriodStart { get; set; }
		public DateTime? IncidentPeriodEnd { get; set; }
		public string EligibleCategories { get; set; }
		public string ApplicantId { get; set; }
		public decimal? FederalSharePercent { get; set; }
		public decimal? NonFederalSharePercent { get; set; }
		/// <summary><see cref="IncidentDeclarationStatuses"/>.</summary>
		public int Status { get; set; }
		public string EligibilityPolicyVersion { get; set; }
		public int? RateScheduleYear { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentDisasterDeclarations";
		[NotMapped] public string IdName => "IncidentDisasterDeclarationId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentDisasterDeclarationId; set => IncidentDisasterDeclarationId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An applicant's project / damage-inventory line by PA category (plan §9.26); cost rows tag it.</summary>
	public class IncidentRecoveryProject : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentRecoveryProjectId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentDisasterDeclarationId { get; set; }
		public string Title { get; set; }
		public string Category { get; set; }
		public string IncidentFacilityId { get; set; }
		public string ContactId { get; set; }
		public string Location { get; set; }
		public string ExternalProjectNumber { get; set; }
		/// <summary><see cref="IncidentDeclarationStatuses"/>.</summary>
		public int Status { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentRecoveryProjects";
		[NotMapped] public string IdName => "IncidentRecoveryProjectId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentRecoveryProjectId; set => IncidentRecoveryProjectId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A disaster-recovery obligation (plan §9.26): request-for-assistance window, damage inventory, project completion, extension expiry. Notified at most once.</summary>
	public class IncidentRecoveryDeadline : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentRecoveryDeadlineId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentDisasterDeclarationId { get; set; }
		public string IncidentRecoveryProjectId { get; set; }
		/// <summary><see cref="IncidentRecoveryObligationKinds"/>.</summary>
		public int Kind { get; set; }
		public DateTime DueOn { get; set; }
		/// <summary><see cref="IncidentGateItemStatuses"/>.</summary>
		public int Status { get; set; }
		public DateTime? NotifiedOn { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentRecoveryDeadlines";
		[NotMapped] public string IdName => "IncidentRecoveryDeadlineId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentRecoveryDeadlineId; set => IncidentRecoveryDeadlineId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
