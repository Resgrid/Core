using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Planning
{
	/// <summary>
	/// shifts.approvals (plan §7.6): signups and trades waiting for the viewer's decision — the Shifts page's approvals
	/// badge (supervised signups + trades the viewer approves under setting 121, ShiftTradeApproverSettings).
	/// </summary>
	public sealed class ShiftApprovalsWidgetData
	{
		/// <summary>False when the viewer neither supervises a group nor holds a trade approver role (the Approvals page refuses them).</summary>
		public bool HasApprovals { get; set; }

		public int PendingSignupCount { get; set; }

		public int PendingTradeCount { get; set; }

		/// <summary>Soonest first, capped.</summary>
		public List<ShiftApprovalItem> Items { get; set; } = new List<ShiftApprovalItem>();

		public string ApprovalsUrl { get; set; }
	}

	public sealed class ShiftApprovalItem
	{
		/// <summary>"signup" or "trade".</summary>
		public string Kind { get; set; }

		/// <summary>ShiftSignupId or ShiftSignupTradeId.</summary>
		public int Id { get; set; }

		public string ShiftName { get; set; }

		public string Color { get; set; }

		public string GroupName { get; set; }

		/// <summary>The member signing up, or the member taking the trade.</summary>
		public string UserName { get; set; }

		/// <summary>The member giving up the slot (trades only).</summary>
		public string RequesterName { get; set; }

		public DateTime StartUtc { get; set; }

		public DateTime EndUtc { get; set; }

		public string Url { get; set; }
	}
}
