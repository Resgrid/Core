using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>Records module state for one department (flag plus append-only cutover fact).</summary>
	public class RecordsModuleState
	{
		public int DepartmentId { get; set; }

		/// <summary>Records.System evaluates on for this department.</summary>
		public bool FlagEnabled { get; set; }

		/// <summary>An RmsDepartmentCutover row exists.</summary>
		public bool Activated { get; set; }

		public DateTime? ActivatedOn { get; set; }

		public int? CutoverId { get; set; }

		public RmsDepartmentCutoverState? CutoverState { get; set; }

		/// <summary>True when every legacy Log/UnitLog mutation must be denied (cutover active).</summary>
		public bool LegacyWritesBlocked { get; set; }

		/// <summary>Records routes are usable: flag on and cutover active.</summary>
		public bool RecordsUsable => FlagEnabled && Activated && CutoverState == RmsDepartmentCutoverState.Active;
	}

	/// <summary>One row of the before/after Permission table shown at activation (registry section 4.6).</summary>
	public class RecordsPermissionMappingRow
	{
		public PermissionTypes Source { get; set; }
		public PermissionTypes Target { get; set; }
		public bool SourceRowExists { get; set; }
		public int? SourceAction { get; set; }
		public string SourceData { get; set; }
		public bool SourceLockToGroup { get; set; }
		/// <summary>The action the target will evaluate after activation: the copied row, or the no-row default.</summary>
		public PermissionActions EffectiveAction { get; set; }
		public string Note { get; set; }
	}

	/// <summary>Everything the administrator sees before confirming activation (RMS plan section 4.1).</summary>
	public class RecordsActivationPreview
	{
		public int DepartmentId { get; set; }
		public bool FlagEnabled { get; set; }
		public bool AlreadyActivated { get; set; }
		public int LegacyLogCount { get; set; }
		public int LegacyUnitLogCount { get; set; }
		/// <summary>Rows with the retired LogType = 3 (Event); the Logs module keeps rendering them.</summary>
		public int LegacyEventTypeLogCount { get; set; }
		public string SourceChecksum { get; set; }
		public List<RecordsPermissionMappingRow> PermissionMapping { get; set; } = new List<RecordsPermissionMappingRow>();
		/// <summary>Suggested LockToGroup for ViewGroupRecords, read from ViewGroupUsers; never applied silently.</summary>
		public bool SuggestedViewGroupRecordsLockToGroup { get; set; }
		/// <summary>NotApplicable when the Protected Data subsystem is absent; otherwise the state name (plan section 5.8).</summary>
		public string ProtectedDataPreflight { get; set; }
		public List<string> Blockers { get; set; } = new List<string>();
		public bool CanActivate => !AlreadyActivated && Blockers.Count == 0;
	}

	public class RecordsActivationResult
	{
		public bool Success { get; set; }
		public string Error { get; set; }
		public RmsDepartmentCutover Cutover { get; set; }
		public static RecordsActivationResult Failed(string error) => new RecordsActivationResult { Success = false, Error = error };
	}

	/// <summary>Rollback decision frame outcome (RMS plan section 4.1).</summary>
	public enum RecordsRollbackOutcome
	{
		CleanRevert = 1,
		DrainAndRevert = 2,
		NoRollback = 3
	}

	/// <summary>
	/// Thrown by the legacy Log/UnitLog service boundary after a department has activated Records
	/// (RMS plan section 4.1). The message is a stable, user-safe statement.
	/// </summary>
	public class RecordsLegacyWriteBlockedException : InvalidOperationException
	{
		public const string StableMessage = "Records is enabled for this department; legacy Logs are read-only.";

		public RecordsLegacyWriteBlockedException(int departmentId, string context)
			: base(StableMessage)
		{
			DepartmentId = departmentId;
			Context = context;
		}

		public int DepartmentId { get; }
		public string Context { get; }
	}

	/// <summary>Thrown when a draft save or transition presents a stale RowVersion (ETag).</summary>
	public class RecordConcurrencyException : InvalidOperationException
	{
		public RecordConcurrencyException(string recordId, long expected, long current)
			: base($"Record {recordId} changed since it was loaded (expected version {expected}, current {current}).")
		{
			RecordId = recordId;
			ExpectedRowVersion = expected;
			CurrentRowVersion = current;
		}

		public string RecordId { get; }
		public long ExpectedRowVersion { get; }
		public long CurrentRowVersion { get; }
	}

	/// <summary>Thrown when a lifecycle transition is not permitted by the Record's preset and state.</summary>
	public class RecordTransitionException : InvalidOperationException
	{
		public RecordTransitionException(string recordId, RmsRecordState from, RmsRecordState to, string reason = null)
			: base($"Record {recordId} cannot move from {from} to {to}{(reason == null ? string.Empty : ": " + reason)}.")
		{
			RecordId = recordId;
			From = from;
			To = to;
		}

		public string RecordId { get; }
		public RmsRecordState From { get; }
		public RmsRecordState To { get; }
	}

	public class RecordParticipantInput
	{
		public string UserId { get; set; }
		public int? UnitId { get; set; }
		public string Role { get; set; }
	}

	public class RecordUnitResponseInput
	{
		public int UnitId { get; set; }
		public DateTime? Dispatched { get; set; }
		public DateTime? Enroute { get; set; }
		public DateTime? OnScene { get; set; }
		public DateTime? Released { get; set; }
		public DateTime? InQuarters { get; set; }
	}

	/// <summary>Officer-authored historical Call created and linked from an existing Run draft.</summary>
	public class RecordNewCallInput
	{
		public string Name { get; set; }
		public string Address { get; set; }
		public string Nature { get; set; }
		public DateTime OccurredOnUtc { get; set; }
	}

	/// <summary>Draft create/save input for a locked Logs-parity definition.</summary>
	public class RecordDraftInput
	{
		public RecordUdfInput CustomFields { get; set; }
		/// <summary>One of <see cref="RmsDefinitionKeys"/>; required on create.</summary>
		public string DefinitionKey { get; set; }
		public int? CallId { get; set; }
		public int? StationGroupId { get; set; }
		public string ExternalId { get; set; }
		public DateTime? StartedOn { get; set; }
		public DateTime? EndedOn { get; set; }
		/// <summary>Typed fields; only the columns relevant to the definition are honored.</summary>
		public RmsOperationalRecordDetail Details { get; set; }
		public List<RecordParticipantInput> Participants { get; set; } = new List<RecordParticipantInput>();
		public List<RecordUnitResponseInput> Units { get; set; } = new List<RecordUnitResponseInput>();
		/// <summary>Client-supplied GUID for offline-created drafts; server-assigned when null.</summary>
		public string ClientRecordId { get; set; }
		public string IdempotencyKey { get; set; }
		public RmsOriginClient OriginClient { get; set; } = RmsOriginClient.Web;
		/// <summary>Recorded reason when the author continues past a duplicate warning.</summary>
		public string DuplicateContinueReason { get; set; }
	}

	/// <summary>A hydrated Record: header, working/revision details, participants, units, attachment metadata.</summary>
	public class RecordAggregate
	{
		public RecordUdfSection CustomFields { get; set; }
		public RmsOperationalRecord Record { get; set; }
		public RmsOperationalRecordDetail Details { get; set; }
		public List<RmsRecordParticipant> Participants { get; set; } = new List<RmsRecordParticipant>();
		public List<RmsRecordUnitResponse> Units { get; set; } = new List<RmsRecordUnitResponse>();
		public List<RmsRecordAttachment> Attachments { get; set; } = new List<RmsRecordAttachment>();
		public List<RmsRevision> Revisions { get; set; } = new List<RmsRevision>();
		public List<RmsRecordGroupScope> GroupScope { get; set; } = new List<RmsRecordGroupScope>();
	}

	/// <summary>
	/// The complete, server-authored snapshot serialized into RmsRevision.SnapshotJson. Diffs are computed
	/// from two of these; none is ever stored.
	/// </summary>
	public class RecordSnapshot
	{
		public RecordUdfSection CustomFields { get; set; }
		public int SnapshotVersion { get; set; } = 1;
		public List<RmsEvidenceArtifact> Evidence { get; set; } = new List<RmsEvidenceArtifact>();
		public string RecordId { get; set; }
		public int DepartmentId { get; set; }
		public string DefinitionKey { get; set; }
		public int DefinitionVersion { get; set; }
		public int? RecordType { get; set; }
		public string RecordNumber { get; set; }
		public string DraftReference { get; set; }
		public int? StationGroupId { get; set; }
		public int? CallId { get; set; }
		public string ExternalId { get; set; }
		public string AuthorUserId { get; set; }
		public DateTime? StartedOn { get; set; }
		public DateTime? EndedOn { get; set; }
		public RmsOperationalRecordDetail Details { get; set; }
		public List<RmsRecordParticipant> Participants { get; set; } = new List<RmsRecordParticipant>();
		public List<RmsRecordUnitResponse> Units { get; set; } = new List<RmsRecordUnitResponse>();
		public List<RmsRecordAttachment> Attachments { get; set; } = new List<RmsRecordAttachment>();
	}

	/// <summary>One changed field in an on-demand revision diff.</summary>
	public class RecordFieldDiff
	{
		public string Section { get; set; }
		public string FieldKey { get; set; }
		public string FieldLabel { get; set; }
		public string OldValue { get; set; }
		public string NewValue { get; set; }
		/// <summary>True when the field is restricted and the viewer lacks RecordRestricted_View: values are withheld.</summary>
		public bool Withheld { get; set; }
	}

	public class DomainEventOutboxHealth
	{
		public int Pending { get; set; }
		public int Failed { get; set; }
		public DateTime? OldestPendingCreatedOn { get; set; }
		public TimeSpan? Backlog { get; set; }
	}

	/// <summary>Producer-owned event handed to the outbox; the outbox adds identity, sequence and delivery state.</summary>
	public class DomainEventEnvelope
	{
		public string EventName { get; set; }
		public int SchemaVersion { get; set; } = 1;
		public string AggregateType { get; set; }
		public string AggregateId { get; set; }
		public int? AggregateVersion { get; set; }
		public WorkflowTriggerEventType? Trigger { get; set; }
		/// <summary>The safe, already-projected payload (never a protected-candidate value).</summary>
		public object Payload { get; set; }
		public string CorrelationId { get; set; }
		public string CausationId { get; set; }
		public RmsOriginClient OriginClient { get; set; }
		public DateTime? OccurredOn { get; set; }
	}
}
