using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Dashboard;

namespace Resgrid.Services.Dashboard
{
	public static partial class DashboardWidgetCatalog
	{
		/// <summary>
		/// Incident Back Office widgets (home-dashboard-redesign-plan.md §7.10, add-on 6). Visible when the module is on (flag + switch,
		/// IsEnabledAsync — the nav rule) and the desk's read claim holds; a lapsed add-on still renders read-only, access level None is
		/// refused by the provider (IncidentWidgetSupport.RequireReadAsync), as the desk base redirects it to billing.
		/// </summary>
		private static partial IEnumerable<DashboardWidgetDefinition> IncidentBackOfficeWidgets()
		{
			yield return new DashboardWidgetDefinition
			{
				Key = "ibo.activations",
				Category = DashboardWidgetCategories.IncidentBackOffice,
				Icon = "fa-sitemap",
				ModuleUrl = "/User/IncidentBackOffice",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 300,
				Settings = new[] { Number("maxRows", 5, 1, 20) },
				Gates = IncidentGates()
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "ibo.desk",
				Category = DashboardWidgetCategories.IncidentBackOffice,
				Icon = "fa-th-large",
				ModuleUrl = "/User/IncidentBackOffice/Command",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L, DashboardWidgetSizes.Xl),
				DefaultSize = DashboardWidgetSizes.L,
				DefaultRefreshSeconds = 300,
				SupportsMultipleInstances = true,
				Settings = new[] { ActivationSetting(), Select("desk", "operations", "operations", "logistics", "finance") },
				Gates = IncidentGates()
			};

			// The resource table's read rule is IncidentBackOffice_View (the Operations desk lists it).
			yield return new DashboardWidgetDefinition
			{
				Key = "ibo.resources",
				Category = DashboardWidgetCategories.IncidentBackOffice,
				Icon = "fa-truck",
				ModuleUrl = "/User/IncidentOperations",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 300,
				Settings = new[] { ActivationSetting() },
				Gates = IncidentGates()
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "ibo.checkIn",
				Category = DashboardWidgetCategories.IncidentBackOffice,
				Icon = "fa-sign-in",
				ModuleUrl = "/User/IncidentParticipants/Enrollment",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 120,
				Settings = new[] { ActivationSetting(), Number("maxRows", 5, 1, 20) },
				Gates = IncidentGates("IncidentParticipant_View")
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "ibo.finance",
				Category = DashboardWidgetCategories.IncidentBackOffice,
				Icon = "fa-money",
				ModuleUrl = "/User/IncidentFinance",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 900,
				Settings = new[] { ActivationSetting() },
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "IncidentBackOffice_View", "IncidentCost_View" },
					Access = new[] { DashboardAccessChecks.IncidentBackOfficeEnabled, DashboardAccessChecks.IncidentBusinessEnabled },
					Addon = PlanAddonTypes.IncidentBackOffice
				}
			};

			// The Coordination desk admits CoordinationBoard_View or IncidentBackOffice_View; the board table itself reads with
			// CoordinationBoard_View, which is the rule that shows boards.
			yield return new DashboardWidgetDefinition
			{
				Key = "ibo.coordination",
				Category = DashboardWidgetCategories.IncidentBackOffice,
				Icon = "fa-th",
				ModuleUrl = "/User/CoordinationBoards",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 300,
				Settings = new[] { Number("maxRows", 5, 1, 20) },
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "CoordinationBoard_View" },
					Access = new[] { DashboardAccessChecks.IncidentBackOfficeEnabled, DashboardAccessChecks.IncidentCoordinationEnabled },
					Addon = PlanAddonTypes.IncidentBackOffice
				}
			};
		}

		private static DashboardWidgetGates IncidentGates(params string[] extraPolicies)
		{
			var policies = new List<string> { "IncidentBackOffice_View" };
			policies.AddRange(extraPolicies);
			return new DashboardWidgetGates
			{
				Policies = policies,
				Access = new[] { DashboardAccessChecks.IncidentBackOfficeEnabled },
				Addon = PlanAddonTypes.IncidentBackOffice
			};
		}

		/// <summary>The activation an IBO widget shows: one of the department's open activations, or "recent" (the newest opened).</summary>
		private static DashboardWidgetSetting ActivationSetting() => new DashboardWidgetSetting
		{
			Name = "activation",
			Type = DashboardSettingTypes.Select,
			OptionsSource = DashboardOptionSources.IncidentActivations,
			Default = "recent"
		};
	}
}
