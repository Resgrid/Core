using System.Collections.Generic;
using Resgrid.Model.Dashboard;

namespace Resgrid.Services.Dashboard
{
	public static partial class DashboardWidgetCatalog
	{
		/// <summary>Map and situational awareness widgets (home-dashboard-redesign-plan.md §7.4).</summary>
		private static partial IEnumerable<DashboardWidgetDefinition> AwarenessWidgets()
		{
			// The markers come from v4 Mapping/GetMapDataAndMarkers (Call_View), the endpoint rg-map uses; the Mapping
			// module switch hides the nav item.
			yield return new DashboardWidgetDefinition
			{
				Key = "map.operations",
				Category = DashboardWidgetCategories.Awareness,
				Icon = "fa-map-marker",
				ModuleUrl = "/User/Mapping",
				AllowedSizes = Sizes(DashboardWidgetSizes.L, DashboardWidgetSizes.Xl, DashboardWidgetSizes.Full),
				DefaultSize = DashboardWidgetSizes.L,
				RealtimeTopics = Topics(DashboardTopics.Calls, DashboardTopics.UnitStatus, DashboardTopics.PersonnelStatus),
				DefaultRefreshSeconds = 60,
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Call_View" },
					Access = new[] { DashboardAccessChecks.MappingEnabled }
				},
				Settings = new[]
				{
					Toggle("showCalls", true),
					Toggle("showUnits", true),
					Toggle("showPersonnel", true),
					Toggle("showStations", true),
					Toggle("showPois", false),
					Toggle("showLayers", true),
					Number("zoom", 0, 0, 18)
				}
			};

			// MapVisualizationsController is Call_View; the visualizations live under Mapping.
			yield return new DashboardWidgetDefinition
			{
				Key = "map.visualization",
				Category = DashboardWidgetCategories.Awareness,
				Icon = "fa-map",
				ModuleUrl = "/User/MapVisualizations",
				AllowedSizes = Sizes(DashboardWidgetSizes.L, DashboardWidgetSizes.Xl),
				DefaultSize = DashboardWidgetSizes.L,
				DefaultRefreshSeconds = 900,
				MinRefreshSeconds = 300,
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Call_View" },
					Access = new[] { DashboardAccessChecks.MappingEnabled }
				},
				Settings = new[] { SelectFrom("visualization", DashboardOptionSources.MapVisualizations) }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "weather.current",
				Category = DashboardWidgetCategories.Awareness,
				Icon = "fa-cloud",
				ModuleUrl = "/User/WeatherAlerts",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 900,
				MinRefreshSeconds = 900,
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Department_View" },
					Access = new[] { DashboardAccessChecks.WeatherConfigured }
				},
				Settings = new[] { Select("units", "imperial", "imperial", "metric") }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "weather.alerts",
				Category = DashboardWidgetCategories.Awareness,
				Icon = "fa-exclamation-triangle",
				ModuleUrl = "/User/WeatherAlerts",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 300,
				// Public NWS/EC/MeteoAlarm alerts; the web app registers no WeatherAlert_View policy (the alerts page is an admin settings page).
				Gates = new DashboardWidgetGates { Policies = new[] { "Department_View" } },
				Settings = new[]
				{
					Select("minSeverity", "all", "all", "extreme", "severe", "moderate"),
					Number("maxItems", 5, 1, 20)
				}
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "general.clock",
				Category = DashboardWidgetCategories.Awareness,
				Icon = "fa-clock-o",
				// No module of its own; the guard wants every widget to link somewhere, and home is always reachable.
				ModuleUrl = "/User/Home/Dashboard",
				AllowedSizes = Sizes(DashboardWidgetSizes.Xs, DashboardWidgetSizes.S),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 3600,
				Settings = new[]
				{
					Select("format", "auto", "auto", "12h", "24h"),
					Toggle("showSeconds", false),
					Toggle("showUtc", true),
					Toggle("showLocal", true)
				}
			};
		}
	}
}
