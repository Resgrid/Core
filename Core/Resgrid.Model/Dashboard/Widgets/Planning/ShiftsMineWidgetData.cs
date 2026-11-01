using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Planning
{
	/// <summary>shifts.mine (plan §7.6): the member's upcoming shift signups in this department and the open trade requests sent to them (the Your Shifts page).</summary>
	public sealed class ShiftsMineWidgetData
	{
		public List<ShiftsMineSignup> Signups { get; set; } = new List<ShiftsMineSignup>();

		public List<ShiftsMineTrade> TradeRequests { get; set; } = new List<ShiftsMineTrade>();

		public string YourShiftsUrl { get; set; }
	}

	public sealed class ShiftsMineSignup
	{
		public int ShiftSignupId { get; set; }

		public int ShiftId { get; set; }

		public int? ShiftDayId { get; set; }

		public string ShiftName { get; set; }

		public string Color { get; set; }

		public string GroupName { get; set; }

		public DateTime StartUtc { get; set; }

		public DateTime EndUtc { get; set; }

		/// <summary>Waiting for a supervisor's approval.</summary>
		public bool ApprovalPending { get; set; }

		/// <summary>The member asked to trade this signup away.</summary>
		public bool HasTrade { get; set; }

		public string Url { get; set; }
	}

	public sealed class ShiftsMineTrade
	{
		public int ShiftSignupTradeId { get; set; }

		public string ShiftName { get; set; }

		public string Color { get; set; }

		public DateTime StartUtc { get; set; }

		public DateTime EndUtc { get; set; }

		public string RequesterName { get; set; }

		public string Url { get; set; }
	}
}
