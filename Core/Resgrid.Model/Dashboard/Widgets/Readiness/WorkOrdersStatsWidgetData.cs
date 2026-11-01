using System;

namespace Resgrid.Model.Dashboard.Widgets.Readiness
{
	/// <summary>workOrders.stats (plan §7.7): the work order Reports page headline figures over the last N days (viewer's read scope).</summary>
	public sealed class WorkOrdersStatsWidgetData
	{
		public DateTime FromUtc { get; set; }

		public DateTime UntilUtc { get; set; }

		public int Total { get; set; }

		public int Open { get; set; }

		public int Overdue { get; set; }

		/// <summary>Preventive work due in the window and how much of it was done on time.</summary>
		public int PreventiveDue { get; set; }

		public int PreventiveOnTime { get; set; }

		public decimal? PreventiveCompliancePercent { get; set; }

		public decimal? MeanTimeToRepairHours { get; set; }

		public decimal DowntimeHours { get; set; }

		public int ResponseSlaBreaches { get; set; }

		public int RepairSlaBreaches { get; set; }

		/// <summary>Open work orders by WorkOrderPriority (Low, Normal, High, Emergency).</summary>
		public int[] OpenByPriority { get; set; } = new int[4];

		/// <summary>Open work orders by age: 0–6, 7–29, 30–89, 90+ days.</summary>
		public int[] OpenByAge { get; set; } = new int[4];
	}
}
