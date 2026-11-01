namespace Resgrid.Model.Dashboard.Widgets.Records
{
	/// <summary>records.neris (plan §7.8): incident report filing pipeline counts (the Records dashboard incident queues).</summary>
	public sealed class RecordsNerisWidgetData
	{
		public int Incomplete { get; set; }

		public int AwaitingReview { get; set; }

		/// <summary>Finalized and queued or in flight to NERIS.</summary>
		public int Submitted { get; set; }

		public int Accepted { get; set; }

		/// <summary>Rejected by NERIS and not yet corrected.</summary>
		public int Rejected { get; set; }
	}
}
