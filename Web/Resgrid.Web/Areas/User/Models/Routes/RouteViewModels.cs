using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Routes
{
	public class RouteIndexView : BaseUserModel
	{
		public List<RoutePlan> Plans { get; set; }
		public RouteIndexView() { Plans = new List<RoutePlan>(); }
	}

	public class RouteNewView : BaseUserModel
	{
		public RoutePlan Plan { get; set; }
		public List<Unit> Units { get; set; }
		public RouteNewView()
		{
			Plan = new RoutePlan();
			Units = new List<Unit>();
		}
	}

	public class RouteEditView : BaseUserModel
	{
		public RoutePlan Plan { get; set; }
		public List<RouteStop> Stops { get; set; }
		public List<RouteSchedule> Schedules { get; set; }
		public List<Unit> Units { get; set; }
		public RouteEditView()
		{
			Plan = new RoutePlan();
			Stops = new List<RouteStop>();
			Schedules = new List<RouteSchedule>();
			Units = new List<Unit>();
		}
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
		public RouteInstanceDetailView()
		{
			Instance = new RouteInstance();
			Plan = new RoutePlan();
			Stops = new List<RouteInstanceStop>();
		}
	}
}
