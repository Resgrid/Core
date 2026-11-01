using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Operations
{
	/// <summary>
	/// calls.kpis (plan §7.1): call activity tiles. Active, pending and scheduled follow the viewer's dispatch scope like the
	/// Dispatch lists; today and the 7-day series count every call the department logged (UTC days), like the call archive.
	/// </summary>
	public sealed class CallsKpisWidgetData
	{
		public int ActiveCount { get; set; }

		/// <summary>Calls logged since 00:00 UTC today.</summary>
		public long TodayCount { get; set; }

		/// <summary>Saved-not-dispatched (Pending) calls.</summary>
		public int PendingCount { get; set; }

		/// <summary>Scheduled calls not yet dispatched.</summary>
		public int ScheduledCount { get; set; }

		/// <summary>Calls awaiting a run-card resource; null when the viewer cannot see them (no Call_Create, or conditional run cards off).</summary>
		public int? AwaitingResourcesCount { get; set; }

		/// <summary>Seven UTC days ending today, oldest first, zero-filled.</summary>
		public List<CallsKpisDay> Last7Days { get; set; } = new List<CallsKpisDay>();

		public string ActiveUrl { get; set; } = "/User/Dispatch/Dashboard";

		public string PendingUrl { get; set; } = "/User/Dispatch/PendingCalls";

		public string ScheduledUrl { get; set; } = "/User/Dispatch/ScheduledCalls";

		public string AwaitingResourcesUrl { get; set; }
	}

	public sealed class CallsKpisDay
	{
		public DateTime DayUtc { get; set; }

		public long Count { get; set; }
	}
}
