using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	/// <summary>
	/// Signups and trades waiting for supervisor approval, limited to the groups the caller supervises.
	/// </summary>
	public class ApprovalsView
	{
		public Department Department { get; set; }
		public List<PendingSignupRow> Signups { get; set; } = new List<PendingSignupRow>();
		public List<PendingTradeRow> Trades { get; set; } = new List<PendingTradeRow>();
	}

	public class PendingSignupRow
	{
		public int ShiftSignupId { get; set; }
		public int? ShiftDayId { get; set; }
		public string UserName { get; set; }
		public string Roles { get; set; }
		public string ShiftName { get; set; }
		public string GroupName { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public DateTime SignupTimestamp { get; set; }
	}

	public class PendingTradeRow
	{
		public int ShiftSignupTradeId { get; set; }
		public int? ShiftDayId { get; set; }
		public string ShiftName { get; set; }
		public string GroupName { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public string RequesterName { get; set; }
		public string TakerName { get; set; }
		public string TakerRoles { get; set; }

		/// <summary>For a swap, the day the requester works in return.</summary>
		public DateTime? SwapBackDay { get; set; }
		public string SwapBackShiftName { get; set; }
		public string Note { get; set; }
	}
}
