namespace Resgrid.Model
{
	public enum ShiftRosterSources
	{
		/// <summary>On the shift's standing roster (ShiftPersons) for every day of an Assigned shift.</summary>
		Assigned = 0,

		/// <summary>Signed up for the day themselves.</summary>
		Signup = 1,

		/// <summary>A supervisor put them on this one day.</summary>
		SupervisorAssigned = 2,

		/// <summary>Working this slot because a trade moved it to them.</summary>
		Trade = 3
	}

	/// <summary>
	/// One person on a specific shift day after standing roster, signups, single-day supervisor edits and completed
	/// trades have all been applied. Built by the shifts service; not persisted.
	/// </summary>
	public class ShiftDayRosterEntry
	{
		public string UserId { get; set; }

		/// <summary>The department group (team) the person fills a slot for, when the slot is tied to one.</summary>
		public int? DepartmentGroupId { get; set; }

		public ShiftRosterSources Source { get; set; }

		/// <summary>The signup backing this entry; null for standing-roster entries.</summary>
		public int? ShiftSignupId { get; set; }

		/// <summary>
		/// Waiting for supervisor approval. Pending entries are shown on the day but are not on duty and do not fill needs.
		/// </summary>
		public bool ApprovalPending { get; set; }

		/// <summary>For <see cref="ShiftRosterSources.Trade"/>, whose slot this was before the trade.</summary>
		public string TradedFromUserId { get; set; }

		/// <summary>The trade that put this person here, for <see cref="ShiftRosterSources.Trade"/>.</summary>
		public int? ShiftSignupTradeId { get; set; }

		public bool IsOnDuty()
		{
			return !ApprovalPending;
		}
	}
}
