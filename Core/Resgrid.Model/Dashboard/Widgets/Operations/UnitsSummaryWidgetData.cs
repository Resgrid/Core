using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Operations
{
	/// <summary>units.summary (plan §7.2): visible units by their status's meaning (AvailabilityMatrix) and by status.</summary>
	public sealed class UnitsSummaryWidgetData
	{
		public int Total { get; set; }

		public int Available { get; set; }

		public int Committed { get; set; }

		public int Delayed { get; set; }

		public int Unavailable { get; set; }

		/// <summary>Statuses with no meaning (and custom values the department no longer has).</summary>
		public int Unknown { get; set; }

		/// <summary>Total minus out of service / unavailable.</summary>
		public int InService { get; set; }

		/// <summary>Each status in use with its count, largest first.</summary>
		public List<UnitsSummaryStatus> ByStatus { get; set; } = new List<UnitsSummaryStatus>();

		public string UnitsUrl { get; set; } = "/User/Units/Index";
	}

	public sealed class UnitsSummaryStatus
	{
		public DashboardStatusChip Status { get; set; }

		public int Count { get; set; }
	}
}
