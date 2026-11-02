using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Routes
{
	public class RouteIndexView : BaseUserModel
	{
		public List<RoutePlan> Plans { get; set; }
		public Dictionary<string, int> StopCounts { get; set; }
		public RouteIndexView()
		{
			Plans = new List<RoutePlan>();
			StopCounts = new Dictionary<string, int>();
		}
	}

	public class ArchivedRouteIndexView : BaseUserModel
	{
		public List<RoutePlan> Plans { get; set; }
		public Dictionary<string, int> StopCounts { get; set; }
		public ArchivedRouteIndexView()
		{
			Plans = new List<RoutePlan>();
			StopCounts = new Dictionary<string, int>();
		}
	}

	/// <summary>The route plan editor partials (options, settings script, optimize dialog) work on New and Edit alike.</summary>
	public interface IRouteEditorModel
	{
		RoutePlan Plan { get; }
		List<Unit> Units { get; }
		RouteEditorContext Editor { get; }
	}

	public class RouteNewView : BaseUserModel, IRouteEditorModel
	{
		public RoutePlan Plan { get; set; }
		public List<Unit> Units { get; set; }
		public List<Contact> Contacts { get; set; }
		public string PendingStopsJson { get; set; }

		/// <summary>Stations, unit stations and vehicles, and routing capabilities for the editor; built on every render.</summary>
		public RouteEditorContext Editor { get; set; } = new RouteEditorContext();
		public RouteNewView()
		{
			Plan = new RoutePlan();
			Units = new List<Unit>();
			Contacts = new List<Contact>();
		}
	}

	public class PendingStopDto
	{
		public string Name { get; set; }
		public string Description { get; set; }
		public int StopType { get; set; }
		public int Priority { get; set; }
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
		public string Address { get; set; }
		public int? CallId { get; set; }
		public string PlannedArrival { get; set; }
		public string PlannedDeparture { get; set; }
		public int? DwellMinutes { get; set; }
		public int? GeofenceRadius { get; set; }
		public string ContactName { get; set; }
		public string ContactNumber { get; set; }
		public string ContactId { get; set; }
		public string Notes { get; set; }
	}

	public class RouteEditView : BaseUserModel, IRouteEditorModel
	{
		public RoutePlan Plan { get; set; }
		public List<RouteStop> Stops { get; set; }
		public List<RouteSchedule> Schedules { get; set; }
		public List<Unit> Units { get; set; }
		public List<Contact> Contacts { get; set; }

		/// <summary>Stations, unit stations and vehicles, and routing capabilities for the editor; built on every render.</summary>
		public RouteEditorContext Editor { get; set; } = new RouteEditorContext();
		public RouteEditView()
		{
			Plan = new RoutePlan();
			Stops = new List<RouteStop>();
			Schedules = new List<RouteSchedule>();
			Units = new List<Unit>();
			Contacts = new List<Contact>();
		}
	}

	/// <summary>
	/// What the route plan editor needs beyond the plan (maplibre-mapping-routing-navigation-plan.md §10.6): stations to
	/// start or end at, where each unit is based and its vehicle size (for the live route preview), and what the install's
	/// routing provider can do.
	/// </summary>
	public class RouteEditorContext
	{
		public List<RouteEditorStation> Stations { get; set; } = new List<RouteEditorStation>();

		/// <summary>Unit id to its station (department group) id; units without one are left out.</summary>
		public Dictionary<int, int> UnitStations { get; set; } = new Dictionary<int, int>();

		/// <summary>Unit id to its effective vehicle size (unit value, else its type's); units with none are left out.</summary>
		public Dictionary<int, VehicleProfile> UnitVehicles { get; set; } = new Dictionary<int, VehicleProfile>();

		/// <summary>The provider's durations reflect live traffic, so "Driving with traffic" is offered.</summary>
		public bool TrafficAvailable { get; set; }

		/// <summary>Which vehicle limits the provider applies: "all", "size" or "none".</summary>
		public string VehicleLimits { get; set; } = "none";

		public string Attribution { get; set; }

		/// <summary>Kilometres and metres; false for miles and feet.</summary>
		public bool Metric { get; set; }
	}

	public class RouteEditorStation
	{
		public int Id { get; set; }
		public string Name { get; set; }
		public double? Latitude { get; set; }
		public double? Longitude { get; set; }
	}

	public class RouteDetailView : BaseUserModel
	{
		public RoutePlan Plan { get; set; }
		public List<RouteStop> Stops { get; set; }
		public RouteDetailView()
		{
			Plan = new RoutePlan();
			Stops = new List<RouteStop>();
		}
	}

	public class RouteInstancesView : BaseUserModel
	{
		public RoutePlan Plan { get; set; }
		public List<RouteInstance> Instances { get; set; }
		public RouteInstancesView()
		{
			Plan = new RoutePlan();
			Instances = new List<RouteInstance>();
		}
	}

	public class ActiveRoutesView : BaseUserModel
	{
		public List<RouteInstance> Instances { get; set; }
		public List<RoutePlan> Plans { get; set; }
		public ActiveRoutesView()
		{
			Instances = new List<RouteInstance>();
			Plans = new List<RoutePlan>();
		}
	}

	public class RouteInstanceDetailView : BaseUserModel
	{
		public RouteInstance Instance { get; set; }
		public RoutePlan Plan { get; set; }
		public List<RouteInstanceStop> Stops { get; set; }

		/// <summary>Deviations recorded for the instance (shown only with <see cref="CanSeeTrack"/>).</summary>
		public List<RouteDeviation> Deviations { get; set; } = new List<RouteDeviation>();

		/// <summary>The driven track and deviation positions are the unit's locations: shown under See Unit Locations only.</summary>
		public bool CanSeeTrack { get; set; }
		public RouteInstanceDetailView()
		{
			Instance = new RouteInstance();
			Plan = new RoutePlan();
			Stops = new List<RouteInstanceStop>();
		}
	}

	public class RouteStartView : BaseUserModel
	{
		public RoutePlan Plan { get; set; }
		public List<Unit> Units { get; set; }
		public string RoutePlanId { get; set; }
		public int SelectedUnitId { get; set; }
		public RouteStartView()
		{
			Plan = new RoutePlan();
			Units = new List<Unit>();
		}
	}

	public class RouteDirectionsView : BaseUserModel
	{
		public RoutePlan Plan { get; set; }
		public List<RouteStop> Stops { get; set; }

		/// <summary>The plan's route from the server's routing provider (null when none could be computed).</summary>
		public RoutePlanRouteResult ServerRoute { get; set; }
		public RouteDirectionsView()
		{
			Plan = new RoutePlan();
			Stops = new List<RouteStop>();
		}
	}
}
