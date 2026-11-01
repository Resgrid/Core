namespace Resgrid.Model.Dashboard.Widgets.Records
{
	/// <summary>records.overview (plan §7.8): the Records dashboard work-queue counts (group-scope aware, from IRecordsDashboardService).</summary>
	public sealed class RecordsOverviewWidgetData
	{
		public int Drafts { get; set; }

		public int AwaitingReview { get; set; }

		public int Returned { get; set; }

		/// <summary>Obligations sitting overdue now.</summary>
		public int Overdue { get; set; }

		public int IncidentIncomplete { get; set; }

		public int IncidentAwaitingReview { get; set; }

		public int DisclosuresOpen { get; set; }

		public int DisclosuresOverdue { get; set; }

		/// <summary>Counts that could not be produced (the module page lists them); the widget shows a hint, not the text.</summary>
		public int WarningCount { get; set; }
	}
}
