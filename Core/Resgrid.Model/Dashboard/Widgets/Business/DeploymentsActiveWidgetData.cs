using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Business
{
	/// <summary>
	/// deployments.active (plan §7.9): open deployments, the viewer's own first. Mirrors the Deployments list: a manager
	/// (Deployments_View / admin) sees the department's open deployments, everyone else only those they are rostered on.
	/// </summary>
	public sealed class DeploymentsActiveWidgetData
	{
		/// <summary>The viewer sees every deployment (manager); false = rostered ones only.</summary>
		public bool CanViewAll { get; set; }

		/// <summary>Open deployments the viewer can see (department-wide for a manager).</summary>
		public int OpenCount { get; set; }

		/// <summary>Open deployments the viewer is rostered on.</summary>
		public int MineCount { get; set; }

		/// <summary>Mine first, then by start date (newest first); at most the maxRows setting.</summary>
		public List<DeploymentsActiveRow> Rows { get; set; } = new List<DeploymentsActiveRow>();
	}

	public sealed class DeploymentsActiveRow
	{
		public string DeploymentId { get; set; }

		public string Name { get; set; }

		public string IncidentNumber { get; set; }

		/// <summary>DeploymentStatuses value.</summary>
		public int Status { get; set; }

		/// <summary>DeploymentStatuses name (Planned, Standby, Active, Demobilizing) — W.deployments.active.Status.{name}.</summary>
		public string StatusName { get; set; }

		public DateTime? StartOn { get; set; }

		/// <summary>Whole days since StartOn (0 on the first day); null before it starts.</summary>
		public int? DaysDeployed { get; set; }

		public bool IsMine { get; set; }
	}
}
