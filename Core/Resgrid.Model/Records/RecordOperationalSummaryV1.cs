using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// RecordOperationalSummaryV1 (RMS plan sections 5.1 and 4.7): the authorized, versioned view of one
	/// official revision that Billing, contractor deployment and other downstream consumers read when they
	/// need finalized incident facts. Record and revision identity, Call/reporting-entity correlation, safe
	/// dates, unit and personnel participation snapshots and the correction/amendment status — and nothing
	/// else. Never narrative, never a restricted section, never a protected-candidate value, never a NERIS
	/// payload. A consumer pins <see cref="RevisionId"/> and <see cref="RevisionChecksum"/>; a later
	/// amendment produces a new summary with <see cref="CorrectionStatus"/> on the old one set to
	/// Superseded, and the RecordAmended trigger tells the consumer to re-read.
	/// </summary>
	public class RecordOperationalSummaryV1
	{
		public const int CurrentContractVersion = 1;

		public int ContractVersion { get; set; } = CurrentContractVersion;

		public int DepartmentId { get; set; }

		public string RecordId { get; set; }

		public RmsRecordKind RecordKind { get; set; }

		public string DefinitionKey { get; set; }

		public int DefinitionVersion { get; set; }

		public string RecordNumber { get; set; }

		/// <summary>The official revision this summary was built from; the consumer's pin.</summary>
		public string RevisionId { get; set; }

		public int RevisionNumber { get; set; }

		public string RevisionChecksum { get; set; }

		public DateTime RevisionCreatedOn { get; set; }

		/// <summary>Whether the pinned revision is the record's current official revision (see <see cref="RecordOperationalSummaryCorrectionStatus"/>).</summary>
		public string CorrectionStatus { get; set; }

		/// <summary>The revision that replaced the pinned one, when <see cref="CorrectionStatus"/> is Superseded.</summary>
		public string SupersededByRevisionId { get; set; }

		/// <summary>An amendment draft is open against the current revision; a further summary is expected.</summary>
		public bool AmendmentOpen { get; set; }

		public string State { get; set; }

		public DateTime? VoidedOn { get; set; }

		public int? CallId { get; set; }

		public string CallNumber { get; set; }

		/// <summary>Incident reports only: the department incident number sent to the reporting destination.</summary>
		public string IncidentNumber { get; set; }

		/// <summary>Incident reports only: the reporting entity the report was authored under.</summary>
		public string ReportingEntityId { get; set; }

		/// <summary>Incident reports only: the destination-assigned incident id once accepted.</summary>
		public string ExternalIncidentId { get; set; }

		public int? StationGroupId { get; set; }

		public DateTime? StartedOn { get; set; }

		public DateTime? EndedOn { get; set; }

		public DateTime? FinalizedOn { get; set; }

		public List<RecordOperationalSummaryUnit> Units { get; set; } = new List<RecordOperationalSummaryUnit>();

		public List<RecordOperationalSummaryParticipant> Participants { get; set; } = new List<RecordOperationalSummaryParticipant>();

		public DateTime GeneratedOn { get; set; }
	}

	public static class RecordOperationalSummaryCorrectionStatus
	{
		/// <summary>The pinned revision is the current official revision.</summary>
		public const string Current = "Current";

		/// <summary>A later revision (amendment) is now official; re-read.</summary>
		public const string Superseded = "Superseded";

		/// <summary>The record was voided after the pinned revision; consumers treat the facts as withdrawn.</summary>
		public const string Voided = "Voided";
	}

	/// <summary>Unit response snapshot as it stood in the pinned revision.</summary>
	public class RecordOperationalSummaryUnit
	{
		public int? UnitId { get; set; }
		public string UnitName { get; set; }
		public string UnitType { get; set; }
		public int? StationGroupId { get; set; }
		public DateTime? Dispatched { get; set; }
		public DateTime? Enroute { get; set; }
		public DateTime? OnScene { get; set; }
		public DateTime? Released { get; set; }
		public DateTime? InQuarters { get; set; }
	}

	/// <summary>Personnel participation snapshot as it stood in the pinned revision.</summary>
	public class RecordOperationalSummaryParticipant
	{
		public string UserId { get; set; }
		public string DisplayName { get; set; }
		public int? UnitId { get; set; }
		public string Role { get; set; }
		public int? GroupId { get; set; }
		public DateTime? ParticipationStart { get; set; }
		public DateTime? ParticipationEnd { get; set; }
	}

	/// <summary>Paged feed of summaries for records changed since a point in time (plan section 4.7, analytics egress).</summary>
	public class RecordOperationalSummaryPage
	{
		public List<RecordOperationalSummaryV1> Items { get; set; } = new List<RecordOperationalSummaryV1>();
		public bool HasMore { get; set; }
		/// <summary>Opaque cursor for the next page; null at the end of the feed.</summary>
		public string NextCursor { get; set; }
		public DateTime GeneratedOn { get; set; }
	}

	public class RecordOperationalSummaryQuery
	{
		public const int MaxTake = 200;

		/// <summary>Records whose projection changed after this instant (UTC); null starts from the beginning.</summary>
		public DateTime? ChangedSince { get; set; }

		/// <summary>Cursor from a previous page's <see cref="RecordOperationalSummaryPage.NextCursor"/>.</summary>
		public string Cursor { get; set; }

		public int Take { get; set; } = 50;

		/// <summary>Restrict to one aggregate kind; null returns both operational Records and incident reports.</summary>
		public RmsRecordKind? RecordKind { get; set; }
	}
}
