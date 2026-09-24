using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	public class YourShiftsView
	{
		public Department Department { get; set; }
		public DateTime LocalNow { get; set; }
		public string UserId { get; set; }

		/// <summary>The caller's signups (pending and approved), plus days traded to them.</summary>
		public List<ShiftSignup> Signups { get; set; }

		/// <summary>Recent and upcoming signups a supervisor denied or took the caller off.</summary>
		public List<ShiftSignup> RemovedSignups { get; set; } = new List<ShiftSignup>();

		/// <summary>Trades the caller was asked to take.</summary>
		public List<ShiftSignupTrade> Trades { get; set; }

		/// <summary>Shift signup id to the id of its shift day, for linking to the day page.</summary>
		public Dictionary<int, int> ShiftDayIds { get; set; } = new Dictionary<int, int>();

		public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<int, string> GroupNames { get; set; } = new Dictionary<int, string>();
	}
}
