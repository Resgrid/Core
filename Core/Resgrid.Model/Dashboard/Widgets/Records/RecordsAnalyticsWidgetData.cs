using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Records
{
	/// <summary>records.analytics (plan §7.8): the executive summary's process KPIs over the last N days.</summary>
	public sealed class RecordsAnalyticsWidgetData
	{
		public DateTime StartUtc { get; set; }

		public DateTime EndUtc { get; set; }

		/// <summary>Figures cover only the Records the viewer may open (group-scoped viewer).</summary>
		public bool GroupScoped { get; set; }

		/// <summary>The read hit the analytics row cap.</summary>
		public bool Truncated { get; set; }

		public int OverdueNow { get; set; }

		public int WentOverdue { get; set; }

		public int OpenDrafts { get; set; }

		public int AwaitingReview { get; set; }

		public double? MedianHoursToFinalize { get; set; }

		public double? MedianReviewHours { get; set; }

		/// <summary>Share of finalized Records returned at least once, 0–100.</summary>
		public double? ReturnRatePercent { get; set; }

		/// <summary>Headline figures against the preceding window (Key is the service's KPI key).</summary>
		public List<RecordsAnalyticsKpi> Kpis { get; set; } = new List<RecordsAnalyticsKpi>();

		/// <summary>Records per month (Key "yyyy-MM").</summary>
		public List<RecordsAnalyticsPoint> ByMonth { get; set; } = new List<RecordsAnalyticsPoint>();
	}

	public sealed class RecordsAnalyticsKpi
	{
		public string Key { get; set; }

		public double Current { get; set; }

		public double Prior { get; set; }

		public double? ChangePercent { get; set; }
	}

	public sealed class RecordsAnalyticsPoint
	{
		public string Key { get; set; }

		public string Label { get; set; }

		public int Count { get; set; }
	}
}
