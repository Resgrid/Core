using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Dashboard;

namespace Resgrid.Services.Dashboard
{
	public static partial class DashboardWidgetCatalog
	{
		/// <summary>
		/// Operations widgets (home-dashboard-redesign-plan.md §7.1 calls and dispatch, §7.2 units). Policies are the module
		/// pages' own: Dispatch (Call_View), awaiting resources (Call_Create + the top-bar badge flags), Reports (Reports_View),
		/// incident command (Command_View, plus the commander gate in the provider), Units (Unit_View), Routes (Route_View).
		/// </summary>
		private static partial IEnumerable<DashboardWidgetDefinition> OperationsWidgets()
		{
			yield return new DashboardWidgetDefinition
			{
				Key = "calls.active",
				Category = DashboardWidgetCategories.Operations,
				Icon = "fa-bullhorn",
				ModuleUrl = "/User/Dispatch/Dashboard",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L, DashboardWidgetSizes.Xl, DashboardWidgetSizes.Full),
				DefaultSize = DashboardWidgetSizes.L,
				RealtimeTopics = Topics(DashboardTopics.Calls),
				DefaultRefreshSeconds = 60,
				Settings = new[]
				{
					Number("maxRows", 10, 1, 50),
					MultiFrom("priorities", DashboardOptionSources.CallPriorities),
					SelectFrom("group", DashboardOptionSources.Groups)
				},
				Gates = new DashboardWidgetGates { Policies = new[] { "Call_View" } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "calls.kpis",
				Category = DashboardWidgetCategories.Operations,
				Icon = "fa-bullhorn",
				ModuleUrl = "/User/Dispatch/Dashboard",
				AllowedSizes = Sizes(DashboardWidgetSizes.Xs, DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				RealtimeTopics = Topics(DashboardTopics.Calls, DashboardTopics.Requirements),
				DefaultRefreshSeconds = 60,
				Gates = new DashboardWidgetGates { Policies = new[] { "Call_View" } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "calls.pending",
				Category = DashboardWidgetCategories.Operations,
				Icon = "fa-clock-o",
				ModuleUrl = "/User/Dispatch/PendingCalls",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				RealtimeTopics = Topics(DashboardTopics.Calls),
				DefaultRefreshSeconds = 60,
				Settings = new[]
				{
					Number("maxRows", 10, 1, 50),
					Select("show", "all", "all", "pending", "scheduled")
				},
				Gates = new DashboardWidgetGates { Policies = new[] { "Call_View" } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "calls.awaitingResources",
				Category = DashboardWidgetCategories.Operations,
				Icon = "fa-exclamation-triangle",
				ModuleUrl = "/User/Dispatch/AwaitingResources",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				RealtimeTopics = Topics(DashboardTopics.Requirements, DashboardTopics.Calls),
				DefaultRefreshSeconds = 60,
				Settings = new[] { Number("maxRows", 5, 1, 25) },
				// The top-bar badge's rule (_TopNavbar.cshtml): dispatchers, with run cards and their conditions on.
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Call_Create" },
					Flags = new[] { FeatureFlagKeys.DispatchRunCards, FeatureFlagKeys.DispatchRunCardConditions }
				}
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "calls.myCheckIns",
				Category = DashboardWidgetCategories.Operations,
				Icon = "fa-check-square-o",
				ModuleUrl = "/User/Dispatch/Dashboard",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				RealtimeTopics = Topics(DashboardTopics.Calls),
				DefaultRefreshSeconds = 60,
				Gates = new DashboardWidgetGates { Policies = new[] { "Call_View" } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "calls.volume",
				Category = DashboardWidgetCategories.Operations,
				Icon = "fa-bar-chart",
				ModuleUrl = "/User/Reports/Index",
				AllowedSizes = Sizes(DashboardWidgetSizes.L, DashboardWidgetSizes.Xl),
				DefaultSize = DashboardWidgetSizes.L,
				DefaultRefreshSeconds = 900,
				Settings = new[] { Select("days", "30", "7", "30", "90") },
				// The Reports nav item also needs the Reports module switched on.
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Reports_View" },
					Access = new[] { DashboardAccessChecks.ReportsEnabled }
				}
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "incidentCommand.active",
				Category = DashboardWidgetCategories.Operations,
				Icon = "fa-sitemap",
				ModuleUrl = "/User/Dispatch/Dashboard",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				RealtimeTopics = Topics(DashboardTopics.IncidentCommand, DashboardTopics.Calls),
				DefaultRefreshSeconds = 60,
				Settings = new[] { Number("maxRows", 5, 1, 25) },
				// v4 IncidentCommand/GetCommandList: Command_View, then ICommandAccessService.CanUseCommandAsync (checked by the provider).
				Gates = new DashboardWidgetGates { Policies = new[] { "Command_View" } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "units.status",
				Category = DashboardWidgetCategories.Operations,
				Icon = "fa-road",
				ModuleUrl = "/User/Units/Index",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L, DashboardWidgetSizes.Xl, DashboardWidgetSizes.Full),
				DefaultSize = DashboardWidgetSizes.L,
				RealtimeTopics = Topics(DashboardTopics.UnitStatus, DashboardTopics.UnitRoles),
				DefaultRefreshSeconds = 60,
				SupportsMultipleInstances = true,
				Settings = new[]
				{
					SelectFrom("station", DashboardOptionSources.Stations),
					MultiFrom("unitTypes", DashboardOptionSources.UnitTypes),
					Toggle("hideAvailable"),
					Select("groupBy", "station", "station", "type", "none")
				},
				Gates = new DashboardWidgetGates { Policies = new[] { "Unit_View" } },
				ProtectedFields = new[] { new DashboardProtectedField { FieldId = "unitstates.note", ViewPermission = PermissionTypes.ViewProtectedOperationalData } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "units.summary",
				Category = DashboardWidgetCategories.Operations,
				Icon = "fa-road",
				ModuleUrl = "/User/Units/Index",
				AllowedSizes = Sizes(DashboardWidgetSizes.Xs, DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				RealtimeTopics = Topics(DashboardTopics.UnitStatus),
				DefaultRefreshSeconds = 60,
				Gates = new DashboardWidgetGates { Policies = new[] { "Unit_View" } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "units.alerts",
				Category = DashboardWidgetCategories.Operations,
				Icon = "fa-bell",
				ModuleUrl = "/User/Units/Index",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				RealtimeTopics = Topics(DashboardTopics.UnitAlerts, DashboardTopics.UnitStatus),
				DefaultRefreshSeconds = 60,
				Settings = new[] { Toggle("showMuted", true) },
				Gates = new DashboardWidgetGates { Policies = new[] { "Unit_View" } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "units.routes",
				Category = DashboardWidgetCategories.Operations,
				Icon = "fa-map-signs",
				ModuleUrl = "/User/Routes/ActiveRoutes",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 120,
				Gates = new DashboardWidgetGates { Policies = new[] { "Route_View" } }
			};
		}
	}
}
