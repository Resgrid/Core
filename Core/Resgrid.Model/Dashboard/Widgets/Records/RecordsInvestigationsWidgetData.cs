using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Records
{
	/// <summary>records.investigations (plan §7.8): the viewer's open investigation cases — case number and state only.</summary>
	public sealed class RecordsInvestigationsWidgetData
	{
		public int Open { get; set; }

		public List<RecordsInvestigationItem> Cases { get; set; } = new List<RecordsInvestigationItem>();
	}

	public sealed class RecordsInvestigationItem
	{
		/// <summary>Opens /User/RecordInvestigations/Details?id={CaseId}.</summary>
		public string CaseId { get; set; }

		public string CaseNumber { get; set; }

		/// <summary>RmsInvestigationCaseState: 1 open, 2 active, 3 pending review, 4 closed.</summary>
		public int State { get; set; }

		public DateTime OpenedOn { get; set; }
	}
}
