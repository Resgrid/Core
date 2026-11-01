using System;

namespace Resgrid.Model.Dashboard.Widgets.Business
{
	/// <summary>deployments.timeReports (plan §7.9): daily time reports waiting on an approver, and approved ones not yet billed.</summary>
	public sealed class DeploymentTimeReportsWidgetData
	{
		public int AwaitingApproval { get; set; }

		public int ApprovedUnbilled { get; set; }

		public DateTime? OldestAwaitingOn { get; set; }

		public DateTime? OldestUnbilledApprovedOn { get; set; }
	}
}
