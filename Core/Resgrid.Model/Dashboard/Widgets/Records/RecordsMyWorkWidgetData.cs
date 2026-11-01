using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Records
{
	/// <summary>records.myWork (plan §7.8): unfinalized Records the viewer owns plus their open work assignments.</summary>
	public sealed class RecordsMyWorkWidgetData
	{
		public int OwnedOutstanding { get; set; }

		public int Assignments { get; set; }

		/// <summary>Assignments past their due date.</summary>
		public int AssignmentsOverdue { get; set; }

		/// <summary>Oldest first, at most maxRows.</summary>
		public List<RecordsMyWorkRecord> Records { get; set; } = new List<RecordsMyWorkRecord>();

		/// <summary>Soonest due first, at most maxRows.</summary>
		public List<RecordsMyWorkAssignment> Queue { get; set; } = new List<RecordsMyWorkAssignment>();
	}

	public sealed class RecordsMyWorkRecord
	{
		/// <summary>Opens /User/Records/Details?id={RecordId}.</summary>
		public string RecordId { get; set; }

		/// <summary>The record number, or the draft reference before finalization.</summary>
		public string Number { get; set; }

		public string DefinitionKey { get; set; }

		/// <summary>RmsRecordState: 1 draft, 2 ready for review, 3 returned, 4 approved.</summary>
		public int State { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? ReviewDueOn { get; set; }
	}

	public sealed class RecordsMyWorkAssignment
	{
		public string AssignmentId { get; set; }

		public string RecordId { get; set; }

		/// <summary>RmsWorkAssignmentPurposes: complete, review, correct, attach, acknowledge.</summary>
		public string Purpose { get; set; }

		/// <summary>RmsWorkAssignmentState: 1 open, 2 acknowledged.</summary>
		public int State { get; set; }

		/// <summary>RmsWorkAssigneeKind: 1 person, 2 unit, 3 group, 4 command role, 5 dispatch role.</summary>
		public int AssigneeKind { get; set; }

		public DateTime? DueOn { get; set; }

		public bool Overdue { get; set; }

		public DateTime CreatedOn { get; set; }
	}
}
