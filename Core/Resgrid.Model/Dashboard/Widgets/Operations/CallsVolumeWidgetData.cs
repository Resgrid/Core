using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Operations
{
	/// <summary>calls.volume (plan §7.1): calls logged per UTC day over the window, by type and priority, and the response-time median.</summary>
	public sealed class CallsVolumeWidgetData
	{
		public int Days { get; set; }

		public DateTime StartUtc { get; set; }

		public DateTime EndUtc { get; set; }

		public long TotalCalls { get; set; }

		/// <summary>One point per UTC day, oldest first, zero-filled.</summary>
		public List<CallsVolumeDay> Daily { get; set; } = new List<CallsVolumeDay>();

		/// <summary>Top call types (largest first) plus an "Other" slice when there are more.</summary>
		public List<CallsVolumeSlice> ByType { get; set; } = new List<CallsVolumeSlice>();

		/// <summary>Every priority with calls in the window, with the department's colour.</summary>
		public List<CallsVolumePriority> ByPriority { get; set; } = new List<CallsVolumePriority>();

		/// <summary>Total response time (alarm to arrival) P50 over the window, from the reporting rollups; null without samples.</summary>
		public double? MedianResponseSeconds { get; set; }

		public double? P90ResponseSeconds { get; set; }

		public long ResponseSampleCount { get; set; }

		public string ReportsUrl { get; set; } = "/User/Reports/Index";
	}

	public sealed class CallsVolumeDay
	{
		public DateTime DayUtc { get; set; }

		public long Count { get; set; }
	}

	public sealed class CallsVolumeSlice
	{
		/// <summary>The call type's name; empty for untyped calls; null for the "Other" slice.</summary>
		public string Label { get; set; }

		public long Count { get; set; }

		public bool IsOther { get; set; }
	}

	public sealed class CallsVolumePriority
	{
		public DashboardCallPriority Priority { get; set; }

		public long Count { get; set; }
	}
}
