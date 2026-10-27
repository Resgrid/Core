using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0297 (both dialects).

	/// <summary>A configurable status board (plan §9.25.3a): department or activation scope. Frozen and snapshotted once into an RMS evidence artifact on close.</summary>
	public class CoordinationBoard : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string CoordinationBoardId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string Name { get; set; }
		/// <summary><see cref="CoordinationBoardScopes"/>.</summary>
		public int Scope { get; set; }
		public int DefaultStalenessMinutes { get; set; }
		public bool IsArchived { get; set; }
		public bool IsFrozen { get; set; }
		public DateTime? FrozenOn { get; set; }
		public string SnapshotArtifactId { get; set; }
		public string Description { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "CoordinationBoards";
		[NotMapped] public string IdName => "CoordinationBoardId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => CoordinationBoardId; set => CoordinationBoardId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A lane: a saved query from the lane-type code catalog with a display rule and staleness threshold (plan §9.25.3a). No board designer executes anything.</summary>
	public class CoordinationBoardLane : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string CoordinationBoardLaneId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string CoordinationBoardId { get; set; }
		public string LaneType { get; set; }
		public string Name { get; set; }
		public int SortOrder { get; set; }
		public string FilterJson { get; set; }
		public string DisplayRule { get; set; }
		public int StalenessMinutes { get; set; }
		public bool AlarmOnStale { get; set; }
		public DateTime? LastSourceOn { get; set; }
		public DateTime? StaleSince { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "CoordinationBoardLanes";
		[NotMapped] public string IdName => "CoordinationBoardLaneId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => CoordinationBoardLaneId; set => CoordinationBoardLaneId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A scoped, revocable, audited read of named board lanes for a partner agency (plan §9.25.1). No protected value is ever on a board.</summary>
	public class CoordinationShareGrant : IEntity, IIncidentLedgerEntity, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string CoordinationShareGrantId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string CoordinationBoardId { get; set; }
		public string CoordinationBoardLaneId { get; set; }
		public string PartnerContactId { get; set; }
		public string PartnerName { get; set; }
		public string Scope { get; set; }
		public string TokenHash { get; set; }
		public DateTime ExpiresOn { get; set; }
		public DateTime? RevokedOn { get; set; }
		public string RevokedByUserId { get; set; }
		public DateTime? LastAccessedOn { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "CoordinationShareGrants";
		[NotMapped] public string IdName => "CoordinationShareGrantId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => CoordinationShareGrantId; set => CoordinationShareGrantId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A FEMA community-lifeline component status as of a time (plan §9.25). Append-only history; current is the latest per component.</summary>
	public class IncidentLifelineStatus : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentLifelineStatusId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentLifelines"/>.</summary>
		public int Lifeline { get; set; }
		public string Component { get; set; }
		/// <summary><see cref="IncidentLifelineColors"/>.</summary>
		public int Status { get; set; }
		public DateTime AsOf { get; set; }
		public string Source { get; set; }
		public string Notes { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentLifelineStatuses";
		[NotMapped] public string IdName => "IncidentLifelineStatusId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentLifelineStatusId; set => IncidentLifelineStatusId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An open action with owner and due date (plan §9.25).</summary>
	public class IncidentOpenAction : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentOpenActionId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public string OwnerUserId { get; set; }
		public string OwnerName { get; set; }
		public DateTime? DueOn { get; set; }
		/// <summary><see cref="IncidentOpenActionStatuses"/>.</summary>
		public int Status { get; set; }
		public DateTime? NotifiedOn { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentOpenActions";
		[NotMapped] public string IdName => "IncidentOpenActionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentOpenActionId; set => IncidentOpenActionId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A decision in flight (plan §9.25): question, options, owner and deadline. Once decided it is written to an RMS decision-log record and closes.</summary>
	public class IncidentDecision : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentDecisionId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string Question { get; set; }
		public string OptionsJson { get; set; }
		public string OwnerUserId { get; set; }
		public string OwnerName { get; set; }
		public DateTime? DeadlineOn { get; set; }
		/// <summary><see cref="IncidentDecisionStatuses"/>.</summary>
		public int Status { get; set; }
		public string Decision { get; set; }
		public DateTime? DecidedOn { get; set; }
		public string ClosedToRecordId { get; set; }
		public DateTime? NotifiedOn { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentDecisions";
		[NotMapped] public string IdName => "IncidentDecisionId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentDecisionId; set => IncidentDecisionId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A road closure, shelter, healthcare facility, critical infrastructure or evacuation zone (plan §9.25.3a). Shelters store capacity and occupancy counts only, never persons.</summary>
	public class IncidentSituationalFeature : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentSituationalFeatureId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentSituationalFeatureTypes"/>.</summary>
		public int FeatureType { get; set; }
		public string Name { get; set; }
		/// <summary><see cref="IncidentSituationalStatuses"/>.</summary>
		public int Status { get; set; }
		public string GeometryJson { get; set; }
		public int? CapacityTotal { get; set; }
		public int? CapacityAvailable { get; set; }
		public decimal? ThresholdPercent { get; set; }
		public DateTime AsOf { get; set; }
		public string Source { get; set; }
		public string Notes { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentSituationalFeatures";
		[NotMapped] public string IdName => "IncidentSituationalFeatureId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentSituationalFeatureId; set => IncidentSituationalFeatureId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
