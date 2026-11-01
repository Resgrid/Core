using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Dashboard;

namespace Resgrid.Services.Dashboard
{
	public static partial class DashboardWidgetCatalog
	{
		/// <summary>
		/// Workforce &amp; Business widgets (home-dashboard-redesign-plan.md §7.9). Gates mirror the nav and each page's own checks:
		/// Invoicing shows whenever flag + module are on (a lapsed add-on is read-only); bids, contracts, Cal OES MARS and workforce
		/// need the Business Operations entitlement, exactly as their nav items do. Deployments and time reports are free.
		/// </summary>
		private static partial IEnumerable<DashboardWidgetDefinition> BusinessWidgets()
		{
			// The Deployments list has no policy beyond the flag: managers see every deployment, members the ones they are rostered on
			// (the provider applies that scope).
			yield return new DashboardWidgetDefinition
			{
				Key = "deployments.active",
				Category = DashboardWidgetCategories.Business,
				Icon = "fa-truck",
				ModuleUrl = "/User/Deployments",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 900,
				Settings = new[] { Number("maxRows", 5, 1, 20) },
				Gates = new DashboardWidgetGates { Flags = new[] { FeatureFlagKeys.Deployments } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "deployments.timeReports",
				Category = DashboardWidgetCategories.Business,
				Icon = "fa-clock-o",
				ModuleUrl = "/User/Deployments",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 900,
				Gates = new DashboardWidgetGates { Policies = new[] { "TimeReports_Approve" }, Flags = new[] { FeatureFlagKeys.Deployments } }
			};

			// InvoicingController also requires the Business Operations master flag (IBusinessOperationsAccessService.IsEnabledAsync).
			yield return new DashboardWidgetDefinition
			{
				Key = "invoicing.receivables",
				Category = DashboardWidgetCategories.Business,
				Icon = "fa-file-text-o",
				ModuleUrl = "/User/Invoicing/Aging",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 900,
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Invoicing_View" },
					Flags = new[] { FeatureFlagKeys.BusinessOperations },
					Access = new[] { DashboardAccessChecks.InvoicingVisible },
					Addon = PlanAddonTypes.BusinessOperations
				}
			};

			// Invoicing Settings' Online tab (Invoicing_Update; the panel is for department admins).
			yield return new DashboardWidgetDefinition
			{
				Key = "invoicing.onlinePayments",
				Category = DashboardWidgetCategories.Business,
				Icon = "fa-credit-card",
				ModuleUrl = "/User/Invoicing/Settings?tab=online",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 900,
				Gates = new DashboardWidgetGates
				{
					Role = DashboardRoles.DepartmentAdmin,
					Policies = new[] { "Invoicing_Update" },
					Flags = new[] { FeatureFlagKeys.BusinessOperations, FeatureFlagKeys.OnlinePayments, FeatureFlagKeys.PaymentsStripeConnect },
					Access = new[] { DashboardAccessChecks.InvoicingVisible },
					Addon = PlanAddonTypes.BusinessOperations
				}
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "bids.pipeline",
				Category = DashboardWidgetCategories.Business,
				Icon = "fa-handshake-o",
				ModuleUrl = "/User/Bids",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 3600,
				Settings = new[] { Number("expiringDays", 14, 1, 90) },
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Bids_View" },
					Access = new[] { DashboardAccessChecks.ContractorBillingUsable },
					Addon = PlanAddonTypes.BusinessOperations
				}
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "contracts.expiring",
				Category = DashboardWidgetCategories.Business,
				Icon = "fa-file-text",
				ModuleUrl = "/User/Contracts",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 3600,
				Settings = new[] { Number("days", 60, 7, 365), Number("maxRows", 5, 1, 20) },
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "ServiceContracts_View" },
					Access = new[] { DashboardAccessChecks.ContractorBillingUsable },
					Addon = PlanAddonTypes.BusinessOperations
				}
			};

			// The Cal OES MARS queue is a field action every member reaches (roster-scoped); managers get the agency queue and readiness.
			yield return new DashboardWidgetDefinition
			{
				Key = "costRecovery.queue",
				Category = DashboardWidgetCategories.Business,
				Icon = "fa-balance-scale",
				ModuleUrl = "/User/CalOesMars/Queue",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 900,
				Settings = new[] { Number("maxRows", 5, 1, 20) },
				Gates = new DashboardWidgetGates
				{
					Access = new[] { DashboardAccessChecks.CostRecoveryUsable },
					Addon = PlanAddonTypes.BusinessOperations
				}
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "workforce.fieldCosting",
				Category = DashboardWidgetCategories.Business,
				Icon = "fa-calculator",
				ModuleUrl = "/User/Workforce/CostRuns",
				AllowedSizes = Sizes(DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 3600,
				Settings = new[] { Number("maxRows", 5, 1, 10) },
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "InternalCosts_View" },
					Access = new[] { DashboardAccessChecks.WorkforceUsable },
					Addon = PlanAddonTypes.BusinessOperations
				}
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "workforce.payData",
				Category = DashboardWidgetCategories.Business,
				Icon = "fa-pie-chart",
				ModuleUrl = "/User/Workforce/PayData",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 3600,
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "PayDataReporting_View" },
					Access = new[] { DashboardAccessChecks.PayDataReportingUsable },
					Addon = PlanAddonTypes.BusinessOperations
				}
			};
		}
	}
}
