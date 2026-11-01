namespace Resgrid.Model.Dashboard.Widgets.Business
{
	/// <summary>bids.pipeline (plan §7.9): bids by status and submitted bids about to lapse.</summary>
	public sealed class BidsPipelineWidgetData
	{
		public int Draft { get; set; }

		/// <summary>Sent to the customer, awaiting an answer.</summary>
		public int Submitted { get; set; }

		public int Accepted { get; set; }

		public int Declined { get; set; }

		public int Expired { get; set; }

		public int Withdrawn { get; set; }

		/// <summary>Submitted bids whose ValidUntil falls within ExpiringDays.</summary>
		public int ExpiringSoon { get; set; }

		/// <summary>The expiringDays setting the count used.</summary>
		public int ExpiringDays { get; set; }
	}
}
