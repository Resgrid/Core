using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0290 (both dialects).

	/// <summary>A standing team roster (plan §9.2): county or complex IMT, an all-hazards IMT on rotation, an EOC section roster, a support team. Readiness is computed, never stored.</summary>
	public class DepartmentIncidentTeam : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string DepartmentIncidentTeamId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string Name { get; set; }
		/// <summary><see cref="IncidentTeamTypes"/>.</summary>
		public int TeamType { get; set; }
		public bool IsActive { get; set; } = true;
		public int? OwningGroupId { get; set; }
		public string Description { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "DepartmentIncidentTeams";
		[NotMapped] public string IdName => "DepartmentIncidentTeamId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => DepartmentIncidentTeamId; set => DepartmentIncidentTeamId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An on-call window for a standing team (plan §9.2).</summary>
	public class DepartmentIncidentTeamRotation : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string DepartmentIncidentTeamRotationId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string DepartmentIncidentTeamId { get; set; }
		public DateTime WindowStart { get; set; }
		public DateTime WindowEnd { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "DepartmentIncidentTeamRotations";
		[NotMapped] public string IdName => "DepartmentIncidentTeamRotationId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => DepartmentIncidentTeamRotationId; set => DepartmentIncidentTeamRotationId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One position on a standing roster (plan §9.2); candidates are ranked under it.</summary>
	public class DepartmentIncidentTeamSlot : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string DepartmentIncidentTeamSlotId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string DepartmentIncidentTeamId { get; set; }
		public string IncidentPositionDefinitionId { get; set; }
		public int SlotOrder { get; set; }
		public string Label { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "DepartmentIncidentTeamSlots";
		[NotMapped] public string IdName => "DepartmentIncidentTeamSlotId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => DepartmentIncidentTeamSlotId; set => DepartmentIncidentTeamSlotId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A ranked candidate for a roster slot: a Resgrid user or an external person kept as a Contact, never free text (plan §9.2).</summary>
	public class DepartmentIncidentTeamCandidate : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string DepartmentIncidentTeamCandidateId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string DepartmentIncidentTeamId { get; set; }
		public string DepartmentIncidentTeamSlotId { get; set; }
		/// <summary><see cref="IncidentTeamCandidateKinds"/>.</summary>
		public int CandidateKind { get; set; }
		public int Priority { get; set; }
		public string UserId { get; set; }
		public string ContactId { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "DepartmentIncidentTeamCandidates";
		[NotMapped] public string IdName => "DepartmentIncidentTeamCandidateId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => DepartmentIncidentTeamCandidateId; set => DepartmentIncidentTeamCandidateId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
