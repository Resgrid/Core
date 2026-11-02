using System;

namespace Resgrid.Model
{
	/// <summary>What a navigating unit reported (maplibre-mapping-routing-navigation-plan.md §9.4).</summary>
	public enum UnitNavigationStates
	{
		Started = 0,
		Progress = 1,
		Rerouted = 2,
		Arrived = 3,
		Ended = 4
	}

	/// <summary>
	/// The latest ETA of a unit navigating in the Unit app, kept in Redis for two minutes after its last report. Transient
	/// operational data: never stored in SQL and not protected data (ADP).
	/// </summary>
	public class UnitNavigationState
	{
		public int DepartmentId { get; set; }

		public int UnitId { get; set; }

		/// <summary>The call the unit is navigating to; null when it is navigating a route.</summary>
		public int? CallId { get; set; }

		public string RouteInstanceId { get; set; }

		public string RouteInstanceStopId { get; set; }

		/// <summary><see cref="UnitNavigationStates"/>.</summary>
		public int State { get; set; }

		public DateTime EtaUtc { get; set; }

		public double RemainingSeconds { get; set; }

		public double RemainingMeters { get; set; }

		public bool IsTrafficAware { get; set; }

		public string Provider { get; set; }

		public DateTime ReportedOn { get; set; }

		public string ReportedByUserId { get; set; }
	}
}
