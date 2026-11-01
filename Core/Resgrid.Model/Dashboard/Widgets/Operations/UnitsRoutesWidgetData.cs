using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Operations
{
	/// <summary>units.routes (plan §7.2): route instances in progress or paused, and unacknowledged route deviations (newest first).</summary>
	public sealed class UnitsRoutesWidgetData
	{
		public List<UnitsRouteInstance> ActiveRoutes { get; set; } = new List<UnitsRouteInstance>();

		public List<UnitsRouteDeviation> Deviations { get; set; } = new List<UnitsRouteDeviation>();

		public int ActiveRouteCount { get; set; }

		public int UnacknowledgedDeviationCount { get; set; }

		/// <summary>
		/// POST api/v4/Routes/AcknowledgeDeviation/{id} (Route_Update), reached through the web BFF. 404 when the deviation
		/// is no longer open.
		/// </summary>
		public DashboardWidgetEndpoint AcknowledgeDeviation { get; set; }

		public string ActiveRoutesUrl { get; set; } = "/User/Routes/ActiveRoutes";
	}

	public sealed class UnitsRouteInstance
	{
		public string RouteInstanceId { get; set; }

		public string RoutePlanId { get; set; }

		public string PlanName { get; set; }

		/// <summary>The plan's colour, "#RRGGBB", or null.</summary>
		public string PlanColor { get; set; }

		public int UnitId { get; set; }

		public string UnitName { get; set; }

		/// <summary>RouteInstanceStatus: 1 in progress, 5 paused.</summary>
		public int Status { get; set; }

		public DateTime? StartedOnUtc { get; set; }

		public int StopsCompleted { get; set; }

		public int StopsTotal { get; set; }

		public int DeviationCount { get; set; }

		public string Url { get; set; }
	}

	public sealed class UnitsRouteDeviation
	{
		public string RouteDeviationId { get; set; }

		public string RouteInstanceId { get; set; }

		/// <summary>Null when the deviation belongs to a route that is no longer active.</summary>
		public string PlanName { get; set; }

		public string UnitName { get; set; }

		public DateTime DetectedOnUtc { get; set; }

		public int DeviationType { get; set; }

		public double DistanceMeters { get; set; }

		public string Url { get; set; }
	}
}
