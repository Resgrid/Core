using System.Collections.Generic;
using Resgrid.Model.Dashboard.Widgets.Operations;

namespace Resgrid.Model.Dashboard.Widgets.Personnel
{
	/// <summary>
	/// personnel.statusSummary (plan §7.3): the people the viewer can see (visibility matrix; disabled and hidden members
	/// left out) counted by current status. No status inside the auto-available window counts as the department's Available.
	/// </summary>
	public sealed class PersonnelStatusSummaryWidgetData
	{
		public int Total { get; set; }

		public int Available { get; set; }

		public int Committed { get; set; }

		public int Delayed { get; set; }

		public int Unavailable { get; set; }

		public int Unknown { get; set; }

		/// <summary>Each status in use with its count, largest first.</summary>
		public List<PersonnelSummaryItem> ByStatus { get; set; } = new List<PersonnelSummaryItem>();

		public string PersonnelUrl { get; set; } = "/User/Personnel/Index";
	}

	public sealed class PersonnelSummaryItem
	{
		public DashboardStatusChip Status { get; set; }

		public int Count { get; set; }
	}
}
