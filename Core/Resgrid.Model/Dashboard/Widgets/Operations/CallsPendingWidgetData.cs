using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Operations
{
	/// <summary>calls.pending (plan §7.1): calls waiting to be dispatched, pending first (oldest first) then scheduled (soonest first).</summary>
	public sealed class CallsPendingWidgetData
	{
		public int PendingCount { get; set; }

		public int ScheduledCount { get; set; }

		public List<CallsPendingWidgetRow> Calls { get; set; } = new List<CallsPendingWidgetRow>();

		/// <summary>
		/// "Dispatch now": POST /User/Dispatch/DispatchCallNow, form body callId (and optional returnTo), header
		/// X-Requested-With: XMLHttpRequest → JSON { success, message, noRecipients }. The action also requires Call_Update
		/// and edit rights on the call; rows say whether to offer it (<see cref="CallsPendingWidgetRow.CanDispatchNow"/>).
		/// </summary>
		public DashboardWidgetEndpoint DispatchNow { get; set; }

		public string PendingListUrl { get; set; } = "/User/Dispatch/PendingCalls";

		public string ScheduledListUrl { get; set; } = "/User/Dispatch/ScheduledCalls";
	}

	public sealed class CallsPendingWidgetRow
	{
		public int CallId { get; set; }

		public string Number { get; set; }

		public string Name { get; set; }

		public string Type { get; set; }

		public string Address { get; set; }

		public DashboardCallPriority Priority { get; set; }

		/// <summary>"pending" (saved, not dispatched) or "scheduled" (dispatches at <see cref="DispatchOnUtc"/>).</summary>
		public string Kind { get; set; }

		public DateTime LoggedOnUtc { get; set; }

		public DateTime? DispatchOnUtc { get; set; }

		/// <summary>The viewer creates calls and may edit this one (department admin or the call's reporter).</summary>
		public bool CanDispatchNow { get; set; }

		public string Url { get; set; }
	}
}
