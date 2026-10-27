using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0290 (both dialects).

	/// <summary>A standard or custom incident position (plan §9.2). Department catalog when IncidentActivationId is null, incident-defined otherwise. Requirements reuse the Phase D certification shape keyed by CertificationType.Code.</summary>
	public class IncidentPositionDefinition : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentPositionDefinitionId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		/// <summary><see cref="IncidentSections"/>.</summary>
		public int Section { get; set; }
		public string Branch { get; set; }
		public string UnitName { get; set; }
		public string Description { get; set; }
		/// <summary><see cref="IncidentPositionSources"/>.</summary>
		public int SourceCatalog { get; set; }
		public string SourceVersion { get; set; }
		/// <summary>Phase D requirement shape: [{Code, IsMandatory, AnyOfGroup, AllowTrainee, GraceDays}].</summary>
		public string RequirementsJson { get; set; }
		public string DefaultWorkspace { get; set; }
		/// <summary>IC board role label this position maps to (read by IC, never written by this module).</summary>
		public int? DefaultIncidentRoleType { get; set; }
		public string SupervisorCode { get; set; }
		public bool IsStandard { get; set; }
		public bool IsActive { get; set; } = true;
		public int SortOrder { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentPositionDefinitions";
		[NotMapped] public string IdName => "IncidentPositionDefinitionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentPositionDefinitionId; set => IncidentPositionDefinitionId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A default desk/queue capability bundle entry for a position (plan §9.2). Confers no authorization: desktop access is ResgridResources policies.</summary>
	public class IncidentPositionCapability : IEntity, IIncidentLedgerEntity, IIncidentEditable
	{
		[Required]
		public string IncidentPositionCapabilityId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentPositionDefinitionId { get; set; }
		public string CapabilityKey { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentPositionCapabilities";
		[NotMapped] public string IdName => "IncidentPositionCapabilityId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentPositionCapabilityId; set => IncidentPositionCapabilityId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An ICS organization chart node for a period (plan §9.2) — section, branch, division/group or unit. Not CommandStructureNode, which stays IC's tactical lane model.</summary>
	public class IncidentOrganizationNode : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentOrganizationNodeId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentOperationalPeriodId { get; set; }
		public string ParentNodeId { get; set; }
		/// <summary><see cref="IncidentOrganizationNodeKinds"/>.</summary>
		public int Kind { get; set; }
		public string Name { get; set; }
		/// <summary><see cref="IncidentSections"/>.</summary>
		public int Section { get; set; }
		public int SortOrder { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentOrganizationNodes";
		[NotMapped] public string IdName => "IncidentOrganizationNodeId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentOrganizationNodeId; set => IncidentOrganizationNodeId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Who holds an incident position, when (plan §9.2, §12.4): a Resgrid user, an incident participant or a placeholder; effective-dated and period-scoped, with acting, deputy, trainee and day/night variants. The IC board role link lives here, never on the IC row.</summary>
	public class IncidentPositionAssignment : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentPositionAssignmentId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentOperationalPeriodId { get; set; }
		public string IncidentPositionDefinitionId { get; set; }
		public string IncidentOrganizationNodeId { get; set; }
		/// <summary><see cref="IncidentAssignmentSubjectTypes"/>.</summary>
		public int SubjectType { get; set; }
		public string UserId { get; set; }
		public string IncidentParticipantId { get; set; }
		public string PlaceholderLabel { get; set; }
		/// <summary><see cref="IncidentAssignmentKinds"/>.</summary>
		public int AssignmentKind { get; set; }
		/// <summary><see cref="IncidentShiftPatterns"/>.</summary>
		public int Shift { get; set; }
		public DateTime StartOn { get; set; }
		public DateTime? EndOn { get; set; }
		public string SupervisorAssignmentId { get; set; }
		public string IncidentRoleAssignmentId { get; set; }
		public bool IsTrainee { get; set; }
		public string TaskBookReference { get; set; }
		public DateTime? TaskBookExpiresOn { get; set; }
		public string TrainingSpecialistAssignmentId { get; set; }
		public int? TraineePriority { get; set; }
		/// <summary><see cref="IncidentQualificationStates"/>.</summary>
		public int QualificationState { get; set; }
		public string QualificationNote { get; set; }
		/// <summary>Set when the assignment was committed by a one-action team activation.</summary>
		public string TeamActivationBatchId { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentPositionAssignments";
		[NotMapped] public string IdName => "IncidentPositionAssignmentId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentPositionAssignmentId; set => IncidentPositionAssignmentId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
