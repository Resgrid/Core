using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Readiness
{
	/// <summary>workOrders.mine (plan §7.7): open work orders assigned to the viewer (the list's "assigned to me" filter).</summary>
	public sealed class WorkOrdersMineWidgetData
	{
		/// <summary>Open assigned work orders on the list's first page.</summary>
		public int Open { get; set; }

		public int Overdue { get; set; }

		/// <summary>The list has more than one page; counts cover the first page.</summary>
		public bool HasMore { get; set; }

		public List<WorkOrdersMineItem> Items { get; set; } = new List<WorkOrdersMineItem>();
	}

	public sealed class WorkOrdersMineItem
	{
		/// <summary>Opens /User/WorkOrders/Detail?id={Id}.</summary>
		public string Id { get; set; }

		public string Number { get; set; }

		public string Title { get; set; }

		/// <summary>WorkOrderStatus.</summary>
		public int Status { get; set; }

		/// <summary>WorkOrderPriority: 0 low, 1 normal, 2 high, 3 emergency.</summary>
		public int Priority { get; set; }

		public DateTime? DueOn { get; set; }

		public DateTime UpdatedOn { get; set; }

		public bool Overdue { get; set; }

		/// <summary>"danger" emergency or overdue, "warning" high, "info" normal, "default" low.</summary>
		public string Tone { get; set; }
	}
}
