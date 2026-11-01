using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Records
{
	/// <summary>logs.recent (plan §7.8): legacy logs written in the last N days by type (departments not on Records).</summary>
	public sealed class LogsRecentWidgetData
	{
		public DateTime SinceUtc { get; set; }

		public int Total { get; set; }

		/// <summary>More logs exist than one summary reads; counts are a lower bound.</summary>
		public bool Truncated { get; set; }

		/// <summary>Counts by LogTypes value, largest first.</summary>
		public List<LogsRecentTypeCount> ByType { get; set; } = new List<LogsRecentTypeCount>();

		/// <summary>Newest first, at most maxRows: header fields only.</summary>
		public List<LogsRecentItem> Recent { get; set; } = new List<LogsRecentItem>();
	}

	public sealed class LogsRecentTypeCount
	{
		/// <summary>LogTypes: 1 run, 2 training, 4 work, 5 meeting, 6 coroner, 7 callback (0 = not set).</summary>
		public int LogType { get; set; }

		public int Count { get; set; }
	}

	public sealed class LogsRecentItem
	{
		/// <summary>Opens /User/Logs/View?logId={LogId}.</summary>
		public int LogId { get; set; }

		public int LogType { get; set; }

		public int? CallId { get; set; }

		public DateTime? StartedOn { get; set; }

		public DateTime LoggedOn { get; set; }
	}
}
