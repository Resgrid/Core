using System;
using System.Collections.Generic;

namespace Resgrid.Model.Invoicing
{
	/// <summary>
	/// Department-wide daily time report counts for the home dashboard's Time Reports widget (home-dashboard-redesign-plan.md §7.9):
	/// one aggregate query instead of loading every report.
	/// </summary>
	public sealed class DeploymentTimeReportCounts
	{
		/// <summary>Submitted reports waiting for an approver.</summary>
		public int AwaitingApproval { get; set; }

		/// <summary>Approved reports not yet on an invoice (the billing engine's input).</summary>
		public int ApprovedUnbilled { get; set; }

		public DateTime? OldestAwaitingOn { get; set; }

		public DateTime? OldestUnbilledApprovedOn { get; set; }
	}

	/// <summary>Bid count for one status, with how many of them run out inside the expiry window (home dashboard Bids widget).</summary>
	public sealed class BidStatusCount
	{
		public int Status { get; set; }

		public int Total { get; set; }

		/// <summary>Bids of this status whose ValidUntil falls inside the requested window.</summary>
		public int Expiring { get; set; }
	}

	/// <summary>The department's bid pipeline (home dashboard Bids widget).</summary>
	public sealed class BidPipelineSummary
	{
		/// <summary>Count per <see cref="BidStatuses"/> value; a status with no bids is absent.</summary>
		public Dictionary<int, int> ByStatus { get; set; } = new Dictionary<int, int>();

		/// <summary>Submitted bids whose ValidUntil falls inside the window.</summary>
		public int ExpiringSoon { get; set; }

		public int Count(BidStatuses status) => ByStatus.TryGetValue((int)status, out var count) ? count : 0;
	}
}
