using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0290 (both dialects).

	/// <summary>One coherent cut of the back-office picture (plan §13.4): metric values carry its id so widgets shown together use one cursor. Rebuildable by definition.</summary>
	public class IncidentOperationalSnapshot : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentOperationalSnapshotId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public long Cursor { get; set; }
		public DateTime TakenOn { get; set; }
		/// <summary><see cref="IncidentSnapshotKinds"/>.</summary>
		public int Kind { get; set; }
		public DateTime? OldestSourceOn { get; set; }
		public int LagSeconds { get; set; }
		public bool DriftDetected { get; set; }
		public string SummaryJson { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentOperationalSnapshots";
		[NotMapped] public string IdName => "IncidentOperationalSnapshotId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentOperationalSnapshotId; set => IncidentOperationalSnapshotId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A versioned metric value at a snapshot cursor (plan §13.4), bucketed by period, night, meal window or horizon. Drill-through re-runs the metric's source query.</summary>
	public class IncidentMetricValue : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentMetricValueId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentOperationalSnapshotId { get; set; }
		public string MetricKey { get; set; }
		public int MetricVersion { get; set; }
		public string Bucket { get; set; }
		public decimal Value { get; set; }
		public string Unit { get; set; }
		public int SourceCount { get; set; }
		public DateTime AsOf { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentMetricValues";
		[NotMapped] public string IdName => "IncidentMetricValueId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentMetricValueId; set => IncidentMetricValueId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Per-source freshness for an activation (plan §13.4): last observed and last change of each IC/Dispatch and ledger source.</summary>
	public class IncidentDataFreshness : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentDataFreshnessId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string SourceKey { get; set; }
		public DateTime? LastObservedOn { get; set; }
		public DateTime? LastChangeOn { get; set; }
		public int StalenessSeconds { get; set; }
		public bool IsStale { get; set; }
		public DateTime UpdatedOn { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentDataFreshness";
		[NotMapped] public string IdName => "IncidentDataFreshnessId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentDataFreshnessId; set => IncidentDataFreshnessId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A saved desk layout and filter set (plan §10 R15, §9.17).</summary>
	public class IncidentSavedDashboardView : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentSoftDeletable, IIncidentVersioned, IIncidentEditable
	{
		[Required]
		public string IncidentSavedDashboardViewId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string UserId { get; set; }
		public string Name { get; set; }
		public string DeskKey { get; set; }
		public string FiltersJson { get; set; }
		public bool IsShared { get; set; }
		public bool IsDeleted { get; set; }
		public int RowVersion { get; set; } = 1;
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentSavedDashboardViews";
		[NotMapped] public string IdName => "IncidentSavedDashboardViewId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentSavedDashboardViewId; set => IncidentSavedDashboardViewId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>As-of cursor and contract version backing a §10A.3 projection contract for an activation.</summary>
	public class IncidentProjectionCursor : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentProjectionCursorId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string ContractKey { get; set; }
		public int ContractVersion { get; set; }
		public long Cursor { get; set; }
		public DateTime? OldestSourceOn { get; set; }
		public DateTime UpdatedOn { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentProjectionCursors";
		[NotMapped] public string IdName => "IncidentProjectionCursorId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentProjectionCursorId; set => IncidentProjectionCursorId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Every incident-position routing resolution (plan §13.8): position, holders resolved and fallback used, so "why didn't I get the arrival notice" is answerable from data.</summary>
	public class IncidentNotificationLog : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentNotificationLogId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public int EventType { get; set; }
		public string PositionCode { get; set; }
		public string HoldersJson { get; set; }
		/// <summary><see cref="IncidentRoutingFallbacks"/>.</summary>
		public int Fallback { get; set; }
		public int RecipientCount { get; set; }
		public string Summary { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentNotificationLogs";
		[NotMapped] public string IdName => "IncidentNotificationLogId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentNotificationLogId; set => IncidentNotificationLogId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
