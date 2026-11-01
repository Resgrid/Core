using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>The header columns of one legacy log: no narrative, contact, cause or any other free-text column is read.</summary>
	public class WorkLogHeader
	{
		public int LogId { get; set; }
		public int? LogType { get; set; }
		public int? StationGroupId { get; set; }
		public int? CallId { get; set; }
		public DateTime? StartedOn { get; set; }
		public DateTime LoggedOn { get; set; }
	}

	/// <summary>Legacy logs written since a moment, counted by <see cref="LogTypes"/>, with the newest headers.</summary>
	public class WorkLogSummary
	{
		public DateTime SinceUtc { get; set; }
		public Dictionary<int, int> CountsByType { get; set; } = new Dictionary<int, int>();
		public int Total { get; set; }
		/// <summary>More logs exist in the window than were read; counts are a lower bound.</summary>
		public bool Truncated { get; set; }
		public List<WorkLogHeader> Recent { get; set; } = new List<WorkLogHeader>();
	}
}
