using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// A shift day with everything needed to show or act on it: the resolved roster, remaining needs per group and
	/// role, and whether it is running right now. Built by the shifts service; not persisted.
	/// </summary>
	public class ShiftDaySchedule
	{
		/// <summary>The day, with <see cref="ShiftDay.Shift"/> set so Start/End resolve.</summary>
		public ShiftDay Day { get; set; }

		public Shift Shift { get; set; }

		public List<ShiftDayRosterEntry> Roster { get; set; } = new List<ShiftDayRosterEntry>();

		/// <summary>Department group id to (personnel role id to people still needed).</summary>
		public Dictionary<int, Dictionary<int, int>> Needs { get; set; } = new Dictionary<int, Dictionary<int, int>>();

		/// <summary>Every signup for the day, including pending and denied ones.</summary>
		public List<ShiftSignup> Signups { get; set; } = new List<ShiftSignup>();

		/// <summary>Trades whose source or swap-back signup is on this day.</summary>
		public List<ShiftSignupTrade> Trades { get; set; } = new List<ShiftSignupTrade>();

		/// <summary>Department-local now falls inside the day's start/end window.</summary>
		public bool IsActive { get; set; }

		public bool IsFilled()
		{
			return Needs == null || Needs.Values.All(x => x.Values.All(v => v <= 0));
		}

		public int OpenSlots()
		{
			return Needs == null ? 0 : Needs.Values.Sum(x => x.Values.Where(v => v > 0).Sum());
		}
	}
}
