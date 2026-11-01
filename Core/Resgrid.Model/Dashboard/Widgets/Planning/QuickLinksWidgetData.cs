using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Planning
{
	/// <summary>
	/// links.quick (plan §7.6): the departments that share data with this one (Department Links, when the plan allows links)
	/// and the member's own links from the widget settings (http/https only; anything else is dropped).
	/// </summary>
	public sealed class QuickLinksWidgetData
	{
		/// <summary>The department's plan includes department links (ILimitsService.CanDepartmentUseLinksAsync).</summary>
		public bool DepartmentLinksAvailable { get; set; }

		public List<QuickLinkDepartment> DepartmentLinks { get; set; } = new List<QuickLinkDepartment>();

		public List<QuickLinkItem> UserLinks { get; set; } = new List<QuickLinkItem>();

		/// <summary>Lines of the links setting that were dropped (not "Label | http(s)://…").</summary>
		public int InvalidUserLinks { get; set; }
	}

	public sealed class QuickLinkDepartment
	{
		public int DepartmentLinkId { get; set; }

		/// <summary>The department sharing its data.</summary>
		public string DepartmentName { get; set; }

		public string Color { get; set; }

		public bool SharesCalls { get; set; }

		public bool SharesUnits { get; set; }

		public bool SharesPersonnel { get; set; }

		public string Url { get; set; }
	}

	public sealed class QuickLinkItem
	{
		public string Label { get; set; }

		/// <summary>An absolute http or https URL.</summary>
		public string Url { get; set; }
	}
}
