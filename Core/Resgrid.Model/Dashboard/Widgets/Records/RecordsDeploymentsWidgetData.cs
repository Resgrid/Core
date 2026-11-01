using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Records
{
	/// <summary>records.deployments (plan §7.8): open deployment Records (external orders) the viewer may open.</summary>
	public sealed class RecordsDeploymentsWidgetData
	{
		public int Open { get; set; }

		/// <summary>Newest first, at most maxRows.</summary>
		public List<RecordsDeploymentItem> Deployments { get; set; } = new List<RecordsDeploymentItem>();
	}

	public sealed class RecordsDeploymentItem
	{
		/// <summary>Opens /User/RecordDeployments (the Records deployment reports list).</summary>
		public string OrderId { get; set; }

		public string RecordId { get; set; }

		public string OrderNumber { get; set; }

		public string IncidentNumber { get; set; }

		/// <summary>RmsExternalOrderStatus: 1 open, 2 mobilized, 3 released, 4 closed out.</summary>
		public int Status { get; set; }

		public DateTime? MobilizedOn { get; set; }

		public DateTime CreatedOn { get; set; }
	}
}
