using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0290 (both dialects).

	/// <summary>The incident header this module owns (plan §9.0): "this department is running the back office for this incident". Kind, complex membership, read-only Call / IC board links, short code, zone, currency, units, lifecycle and the exercise flag. Every incident-scoped table hangs from it.</summary>
	public class IncidentActivation : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentActivationId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		/// <summary><see cref="IncidentActivationKinds"/>.</summary>
		public int Kind { get; set; }
		/// <summary>Complex membership: the parent Complex activation.</summary>
		public string ParentActivationId { get; set; }
		/// <summary>Read-only link to the dispatch Call; RMS per-incident numbering anchor.</summary>
		public int? CallId { get; set; }
		/// <summary>Read-only link to the IC board (one activation per board).</summary>
		public string IncidentCommandId { get; set; }
		public string Name { get; set; }
		/// <summary>Unique per department among live rows; used in local numbering and printed artifacts.</summary>
		public string ShortCode { get; set; }
		/// <summary>ICS complexity Type 1..5.</summary>
		public int? ComplexityType { get; set; }
		public string TimeZoneId { get; set; }
		public string Currency { get; set; }
		public string MeasurementSystem { get; set; }
		/// <summary><see cref="IncidentActivationStatuses"/>.</summary>
		public int Status { get; set; }
		public bool IsExercise { get; set; }
		public string AuthorityProfileSnapshotId { get; set; }
		public string Description { get; set; }
		public DateTime? OpenedOn { get; set; }
		public string OpenedByUserId { get; set; }
		public DateTime? ClosedOn { get; set; }
		public string ClosedByUserId { get; set; }
		public DateTime? SealedOn { get; set; }
		public string SealedByUserId { get; set; }
		public DateTime? ReopenedOn { get; set; }
		public string ReopenedByUserId { get; set; }
		public string ReopenReason { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentActivations";
		[NotMapped] public string IdName => "IncidentActivationId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentActivationId; set => IncidentActivationId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A department's per-domain authority configuration (plan §7): which domains Resgrid owns, which an external system owns, and which are shared and reconciled. An activation snapshots it so its rules cannot change silently mid-incident.</summary>
	public class DepartmentIncidentAuthorityProfile : IEntity, IIncidentLedgerEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string DepartmentIncidentAuthorityProfileId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string Name { get; set; }
		public bool IsDefault { get; set; }
		public string Description { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "DepartmentIncidentAuthorityProfiles";
		[NotMapped] public string IdName => "DepartmentIncidentAuthorityProfileId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => DepartmentIncidentAuthorityProfileId; set => DepartmentIncidentAuthorityProfileId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One authority domain row of a profile or of an activation's snapshot (plan §7.1).</summary>
	public class IncidentDomainAuthority : IEntity, IIncidentLedgerEntity, IIncidentEditable
	{
		[Required]
		public string IncidentDomainAuthorityId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		/// <summary><see cref="IncidentAuthorityOwnerKinds"/>.</summary>
		public int OwnerKind { get; set; }
		public string OwnerId { get; set; }
		/// <summary><see cref="IncidentAuthorityDomains"/>.</summary>
		public int Domain { get; set; }
		/// <summary><see cref="IncidentAuthorityModes"/>.</summary>
		public int Mode { get; set; }
		public string SourceSystem { get; set; }
		/// <summary><see cref="IncidentConnectorStates"/>.</summary>
		public int ConnectorState { get; set; }
		public int Precedence { get; set; }
		public string Notes { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentDomainAuthorities";
		[NotMapped] public string IdName => "IncidentDomainAuthorityId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentDomainAuthorityId; set => IncidentDomainAuthorityId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>The authority profile as it stood when an activation took it (plan §7). Immutable; a new profile choice takes a new snapshot.</summary>
	public class IncidentAuthorityProfileSnapshot : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentAuthorityProfileSnapshotId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string SourceProfileId { get; set; }
		public string Name { get; set; }
		public DateTime TakenOn { get; set; }
		public string TakenByUserId { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentAuthorityProfileSnapshots";
		[NotMapped] public string IdName => "IncidentAuthorityProfileSnapshotId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentAuthorityProfileSnapshotId; set => IncidentAuthorityProfileSnapshotId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A conflict between local and external authority queued for a named owner (plan §7, risk 14).</summary>
	public class IncidentReconciliationItem : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentReconciliationItemId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentAuthorityDomains"/>.</summary>
		public int Domain { get; set; }
		public string SubjectType { get; set; }
		public string SubjectId { get; set; }
		public string SourceSystem { get; set; }
		public string ExternalIdentifier { get; set; }
		public string Description { get; set; }
		/// <summary><see cref="IncidentReconciliationStatuses"/>.</summary>
		public int Status { get; set; }
		public string OwnerUserId { get; set; }
		public DateTime? ResolvedOn { get; set; }
		public string ResolvedByUserId { get; set; }
		public string Resolution { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentReconciliationItems";
		[NotMapped] public string IdName => "IncidentReconciliationItemId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentReconciliationItemId; set => IncidentReconciliationItemId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
