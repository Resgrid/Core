using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Dashboard;

namespace Resgrid.Services.Dashboard
{
	public static partial class DashboardWidgetCatalog
	{
		/// <summary>The Records nav group rule (plan §7.8): flag on, cutover finished (usable), Record_View.</summary>
		private static DashboardWidgetGates RecordsGates(string[] policies = null, string[] flags = null, string[] anyFlags = null,
			DashboardAccessChecks[] access = null, PermissionTypes[] permissions = null) => new DashboardWidgetGates
		{
			Policies = new[] { "Record_View" }.Concat(policies ?? Array.Empty<string>()).ToArray(),
			Flags = new[] { FeatureFlagKeys.RecordsSystem }.Concat(flags ?? Array.Empty<string>()).ToArray(),
			AnyFlags = anyFlags ?? Array.Empty<string>(),
			Access = new[] { DashboardAccessChecks.RecordsUsable }.Concat(access ?? Array.Empty<DashboardAccessChecks>()).ToArray(),
			Permissions = permissions ?? Array.Empty<PermissionTypes>()
		};

		/// <summary>The Records prevention module flags; the prevention widget shows when any is on and renders only its own tiles.</summary>
		internal static readonly string[] RecordsPreventionFlags =
		{
			FeatureFlagKeys.RecordsPreventionOccupancy, FeatureFlagKeys.RecordsPreventionInspections, FeatureFlagKeys.RecordsPreventionHydrants,
			FeatureFlagKeys.RecordsPreventionPermits, FeatureFlagKeys.RecordsPreventionCrr
		};

		/// <summary>Records widgets (home-dashboard-redesign-plan.md §7.8) and the legacy Logs widget.</summary>
		private static partial IEnumerable<DashboardWidgetDefinition> RecordsWidgets()
		{
			yield return new DashboardWidgetDefinition
			{
				Key = "records.overview",
				Category = DashboardWidgetCategories.Records,
				Icon = "fa-file",
				ModuleUrl = "/User/Records/Dashboard",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 300,
				Gates = RecordsGates()
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "records.myWork",
				Category = DashboardWidgetCategories.Records,
				Icon = "fa-list",
				ModuleUrl = "/User/Records",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 300,
				Settings = new[] { Number("maxRows", 5, 1, 20) },
				Gates = RecordsGates()
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "records.neris",
				Category = DashboardWidgetCategories.Records,
				Icon = "fa-paper-plane",
				ModuleUrl = "/User/Records/Dashboard",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 900,
				Gates = RecordsGates(access: new[] { DashboardAccessChecks.NerisWorkflowEnabled })
			};

			// The Quality pages require Record_Review on top of the flag.
			yield return new DashboardWidgetDefinition
			{
				Key = "records.qualityReview",
				Category = DashboardWidgetCategories.Records,
				Icon = "fa-check-circle",
				ModuleUrl = "/User/RecordsQuality",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 300,
				Settings = new[] { Number("maxRows", 5, 1, 20) },
				Gates = RecordsGates(policies: new[] { "Record_Review" }, flags: new[] { FeatureFlagKeys.RecordsQualityReview })
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "records.prevention",
				Category = DashboardWidgetCategories.Records,
				Icon = "fa-building-o",
				ModuleUrl = "/User/RecordsHealth",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L, DashboardWidgetSizes.Xl),
				DefaultSize = DashboardWidgetSizes.L,
				DefaultRefreshSeconds = 900,
				Gates = RecordsGates(anyFlags: RecordsPreventionFlags)
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "records.hydrants",
				Category = DashboardWidgetCategories.Records,
				Icon = "fa-tint",
				ModuleUrl = "/User/RecordHydrants",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 3600,
				Settings = new[] { Number("maxRows", 25, 0, 100) },
				Gates = RecordsGates(flags: new[] { FeatureFlagKeys.RecordsPreventionHydrants })
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "records.crr",
				Category = DashboardWidgetCategories.Records,
				Icon = "fa-bullhorn",
				ModuleUrl = "/User/RecordCrr",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 3600,
				Gates = RecordsGates(flags: new[] { FeatureFlagKeys.RecordsPreventionCrr })
			};

			// Case lists are restricted and membership-scoped by the service: number and state only, never titles or findings.
			yield return new DashboardWidgetDefinition
			{
				Key = "records.investigations",
				Category = DashboardWidgetCategories.Records,
				Icon = "fa-search",
				ModuleUrl = "/User/RecordInvestigations",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 900,
				Settings = new[] { Number("maxRows", 5, 1, 20) },
				Gates = RecordsGates(policies: new[] { "RecordRestricted_View" }, flags: new[] { FeatureFlagKeys.RecordsInvestigations })
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "records.analytics",
				Category = DashboardWidgetCategories.Records,
				Icon = "fa-bar-chart",
				ModuleUrl = "/User/RecordsAnalytics",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 3600,
				Settings = new[] { Number("days", 90, 7, 366) },
				Gates = RecordsGates(flags: new[] { FeatureFlagKeys.RecordsAnalytics })
			};

			// The nav's legal-holds link rule: ManageRecordLegalHold through IRecordsAuthorizationService (the page's policy derives from it).
			yield return new DashboardWidgetDefinition
			{
				Key = "records.legalHolds",
				Category = DashboardWidgetCategories.Records,
				Icon = "fa-lock",
				ModuleUrl = "/User/RecordLegalHolds",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 3600,
				Settings = new[] { Number("maxRows", 5, 1, 20) },
				Gates = RecordsGates(permissions: new[] { PermissionTypes.ManageRecordLegalHold })
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "records.deployments",
				Category = DashboardWidgetCategories.Records,
				Icon = "fa-bar-chart",
				ModuleUrl = "/User/RecordDeployments",
				AllowedSizes = Sizes(DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 900,
				Settings = new[] { Number("maxRows", 5, 1, 20) },
				Gates = RecordsGates()
			};

			// The inverse of the Records group: Logs on and Records not usable (the sidebar's own Logs link), Log_View.
			yield return new DashboardWidgetDefinition
			{
				Key = "logs.recent",
				Category = DashboardWidgetCategories.Records,
				Icon = "fa-file",
				ModuleUrl = "/User/Logs",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 900,
				Settings = new[] { Number("days", 7, 1, 31), Number("maxRows", 5, 0, 20) },
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Log_View" },
					Access = new[] { DashboardAccessChecks.LegacyLogsOnly }
				}
			};
		}
	}
}
