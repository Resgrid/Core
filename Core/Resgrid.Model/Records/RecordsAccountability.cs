using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>Axis of the accountability view (RMS plan section 4.7): who owes a report.</summary>
	public enum RecordsAccountabilityPivot
	{
		Person = 1,
		Group = 2,
		Unit = 3
	}

	/// <summary>One open Record inside an accountability row, enough to drill through and to decide on a reminder.</summary>
	public class RecordsAccountabilityRecord
	{
		public string RecordId { get; set; }
		public string Reference { get; set; }
		public string Summary { get; set; }
		public RmsRecordState State { get; set; }
		public string OwnerUserId { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime? ReviewDueOn { get; set; }
		public DateTime? ReturnedOn { get; set; }
		public bool OverdueReview { get; set; }
		public bool ReturnedNotCorrected { get; set; }
	}

	public class RecordsAccountabilityRow
	{
		/// <summary>User ID, group ID or unit ID depending on the pivot; empty for "unassigned".</summary>
		public string Key { get; set; }
		public string Name { get; set; }
		public int Open { get; set; }
		public int OverdueReviews { get; set; }
		public int ReturnedNotCorrected { get; set; }
		public int FinalizedInWindow { get; set; }
		/// <summary>Mean hours from creation to finalization over the window; null with nothing finalized.</summary>
		public double? AverageHoursToFinalize { get; set; }
		public DateTime? OldestOpenOn { get; set; }
		public List<RecordsAccountabilityRecord> OpenRecords { get; set; } = new List<RecordsAccountabilityRecord>();
	}

	public class RecordsAccountabilityReport
	{
		public RecordsAccountabilityPivot Pivot { get; set; }
		public int WindowDays { get; set; }
		public DateTime GeneratedOn { get; set; }
		public List<RecordsAccountabilityRow> Rows { get; set; } = new List<RecordsAccountabilityRow>();
		public RecordsAccountabilityRow Totals { get; set; } = new RecordsAccountabilityRow();
	}

	public class RecordsReminderResult
	{
		public const string ReasonSent = "Sent";
		public const string ReasonNotOpen = "NotOpen";
		public const string ReasonNoRecipient = "NoRecipient";
		public const string ReasonRecentlyReminded = "RecentlyReminded";
		public const string ReasonLimit = "Limit";
		public const string ReasonFailed = "Failed";

		public string RecordId { get; set; }
		public bool Sent { get; set; }
		public string Reason { get; set; }
		public string RecipientUserId { get; set; }
	}
}
