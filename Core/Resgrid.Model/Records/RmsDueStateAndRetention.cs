using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// What a Record is late for (RMS plan section 4.7, RMS-3). Persisted as an integer; append-only.
	/// </summary>
	public enum RmsRecordObligation
	{
		/// <summary>Submitted for review and not reviewed by its due time (notification 32, trigger 112).</summary>
		Review = 1,

		/// <summary>Returned for correction and not corrected — the record is sitting with its author.</summary>
		Correction = 2,

		/// <summary>Rejected by the reporting destination and not corrected and resubmitted.</summary>
		Submission = 3
	}

	/// <summary>
	/// Where an obligation stands. The evaluation emits only on the transition into <see cref="Overdue"/>, which
	/// is what makes "at most once per record and due-state transition" true.
	/// </summary>
	public enum RmsDueState
	{
		NotDue = 0,
		DueSoon = 1,
		Overdue = 2,
		/// <summary>The obligation was met (reviewed, corrected, resubmitted) or no longer applies.</summary>
		Cleared = 3
	}

	/// <summary>
	/// The persisted due state of one obligation on one Record (registry M0170, RMS-3).
	/// <para>
	/// The plan is explicit that the "at most once per record/due-state transition" guarantee is carried by this
	/// row and never inferred from the last worker run: a missed run must not skip an emission and a repeated run
	/// must not double-emit. The worker compares the state it computes now against
	/// <see cref="LastEmittedState"/> and only emits when they differ.
	/// </para>
	/// </summary>
	public class RmsRecordDueState : IEntity
	{
		public string RmsRecordDueStateId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The operational Record or incident report the obligation is on.</summary>
		public string RecordId { get; set; }

		/// <summary><see cref="RmsRecordKind"/>.</summary>
		public int RecordKind { get; set; }

		/// <summary><see cref="RmsRecordObligation"/>.</summary>
		public int Obligation { get; set; }

		/// <summary>When the obligation falls due; a change to it re-arms emission for the new deadline.</summary>
		public DateTime? DueOn { get; set; }

		/// <summary><see cref="RmsDueState"/> last emitted for this obligation and <see cref="DueOn"/>.</summary>
		public int LastEmittedState { get; set; }

		public DateTime? LastEmittedOn { get; set; }

		/// <summary>Who the obligation currently rests with, snapshotted so the notification has a target.</summary>
		public string ResponsibleUserId { get; set; }

		/// <summary>How many times this obligation has gone overdue across its lifetime; reporting only.</summary>
		public int OverdueCount { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		public object IdValue { get => RmsRecordDueStateId; set => RmsRecordDueStateId = (string)value; }
		public string TableName => "RmsRecordDueStates";
		public string IdName => "RmsRecordDueStateId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Why a hold exists; shown to administrators and carried into the audit when a purge is refused.</summary>
	public static class RmsLegalHoldReasons
	{
		public const string Litigation = "Litigation";
		public const string Investigation = "Investigation";
		public const string PublicRecordsRequest = "PublicRecordsRequest";
		public const string Other = "Other";
	}

	/// <summary>
	/// A legal hold that suspends retention for a Record, a definition, or a whole department date range
	/// (registry M0170, RMS-3). A hold never deletes and never edits; its only effect is that the retention sweep
	/// refuses to purge what it covers, and that refusal is audited rather than silent.
	/// </summary>
	public class RmsRecordLegalHold : IEntity
	{
		public string RmsRecordLegalHoldId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The specific Record held, or null when the hold is a definition/date-range hold.</summary>
		public string RecordId { get; set; }

		/// <summary>The definition held, or null when the hold names a single Record or the whole department.</summary>
		public string DefinitionKey { get; set; }

		/// <summary>Inclusive start of the held period, matched against the Record's started/created time.</summary>
		public DateTime? PeriodStart { get; set; }

		/// <summary>Inclusive end of the held period.</summary>
		public DateTime? PeriodEnd { get; set; }

		/// <summary><see cref="RmsLegalHoldReasons"/>.</summary>
		public string Reason { get; set; }

		public string ReferenceNumber { get; set; }

		public string Notes { get; set; }

		public string PlacedByUserId { get; set; }

		public DateTime PlacedOn { get; set; }

		public string ReleasedByUserId { get; set; }

		public DateTime? ReleasedOn { get; set; }

		public string ReleaseNotes { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		/// <summary>Active while it has not been released.</summary>
		public bool IsActive => !ReleasedOn.HasValue;

		/// <summary>Whether this hold covers a Record of a definition started at a time.</summary>
		public bool Covers(string recordId, string definitionKey, DateTime? startedOn)
		{
			if (!IsActive)
				return false;

			if (!string.IsNullOrWhiteSpace(RecordId))
				return string.Equals(RecordId, recordId, StringComparison.Ordinal);

			if (!string.IsNullOrWhiteSpace(DefinitionKey) && !string.Equals(DefinitionKey, definitionKey, StringComparison.Ordinal))
				return false;

			// A hold with no period covers every date; one with a period needs a date to compare against, and a
			// Record with no date is held rather than purged — the safe reading when the evidence is incomplete.
			if (!PeriodStart.HasValue && !PeriodEnd.HasValue)
				return true;
			if (!startedOn.HasValue)
				return true;

			if (PeriodStart.HasValue && startedOn.Value < PeriodStart.Value)
				return false;
			if (PeriodEnd.HasValue && startedOn.Value > PeriodEnd.Value)
				return false;

			return true;
		}

		public object IdValue { get => RmsRecordLegalHoldId; set => RmsRecordLegalHoldId = (string)value; }
		public string TableName => "RmsRecordLegalHolds";
		public string IdName => "RmsRecordLegalHoldId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsActive" };
	}

	/// <summary>Result of one due-state evaluation sweep (worker 42).</summary>
	public class RecordsDueStateSweepResult
	{
		public int DepartmentsEvaluated { get; set; }
		public int RecordsEvaluated { get; set; }
		public int BecameOverdue { get; set; }
		public int Cleared { get; set; }
		public int NotificationsSent { get; set; }
		public int Errors { get; set; }
		public string Message { get; set; }
	}

	/// <summary>Result of one retention and purge sweep (worker 43).</summary>
	public class RecordsRetentionSweepResult
	{
		public int DepartmentsEvaluated { get; set; }
		public int RecordsEvaluated { get; set; }
		public int RecordsPurged { get; set; }
		/// <summary>SQL purges from this sweep still awaiting committed index erasure; not a total backlog count.</summary>
		public int SearchErasuresPending { get; set; }
		public int AttachmentsPurged { get; set; }
		public int HeldByLegalHold { get; set; }
		public int AttachmentsRescanned { get; set; }
		public int AttachmentsRejectedOnRescan { get; set; }
		public int Errors { get; set; }
		public string Message { get; set; }
	}
}
