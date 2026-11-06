using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Dashboard;

namespace Resgrid.Services.Dashboard
{
	public static partial class DashboardWidgetCatalog
	{
		/// <summary>Readiness widgets (home-dashboard-redesign-plan.md §7.7): checklists, work orders, inventory, certifications.</summary>
		private static partial IEnumerable<DashboardWidgetDefinition> ReadinessWidgets()
		{
			// Free checklists: flag Checklists.System + module (IReadinessAccessService.CanUseChecklistsAsync), the nav's rule.
			yield return new DashboardWidgetDefinition
			{
				Key = "checklists.due",
				Category = DashboardWidgetCategories.Readiness,
				Icon = "fa-check-square-o",
				ModuleUrl = "/User/Checklists/Due",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.S,
				RealtimeTopics = Topics(DashboardTopics.Checklists),
				DefaultRefreshSeconds = 300,
				Settings = new[] { Number("maxRows", 5, 1, 20), Toggle("assignedOnly", true) },
				Gates = new DashboardWidgetGates { Access = new[] { DashboardAccessChecks.ChecklistsUsable } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "checklists.compliance",
				Category = DashboardWidgetCategories.Readiness,
				Icon = "fa-check-square-o",
				ModuleUrl = "/User/Checklists/Compliance",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 900,
				Settings = new[] { Number("days", 30, 7, 90), Number("maxRows", 5, 1, 20) },
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "ChecklistResults_View" },
					Access = new[] { DashboardAccessChecks.ChecklistsUsable }
				}
			};

			// Work orders need the Readiness Pro entitlement exactly as their nav item does (plan §7 add-on display policy).
			yield return new DashboardWidgetDefinition
			{
				Key = "workOrders.stats",
				Category = DashboardWidgetCategories.Readiness,
				Icon = "fa-wrench",
				ModuleUrl = "/User/WorkOrders/Reports",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 300,
				Settings = new[] { Number("days", 90, 7, 365) },
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "WorkOrder_View" },
					Access = new[] { DashboardAccessChecks.MaintenanceUsable },
					Addon = PlanAddonTypes.ReadinessPro
				}
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "workOrders.mine",
				Category = DashboardWidgetCategories.Readiness,
				Icon = "fa-wrench",
				ModuleUrl = "/User/WorkOrders?AssignedToMe=true",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 300,
				Settings = new[] { Number("maxRows", 5, 1, 20) },
				Gates = new DashboardWidgetGates
				{
					Access = new[] { DashboardAccessChecks.MaintenanceUsable },
					Addon = PlanAddonTypes.ReadinessPro
				}
			};

			// Inventory: module switch only (the page itself checks membership; rows follow the inventory authorization rules).
			yield return new DashboardWidgetDefinition
			{
				Key = "inventory.alerts",
				Category = DashboardWidgetCategories.Readiness,
				Icon = "fa-barcode",
				ModuleUrl = "/User/Inventory/Operations?tab=Alerts",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 900,
				Settings = new[] { Number("maxRows", 5, 1, 20) },
				Gates = new DashboardWidgetGates { Access = new[] { DashboardAccessChecks.InventoryEnabled } }
			};

			// Valuation needs the AdjustInventory permission (default: department admins). The gate is the admin role because no
			// dashboard gate can express that inventory permission; the provider still enforces it through the service.
			yield return new DashboardWidgetDefinition
			{
				Key = "inventory.valuation",
				Category = DashboardWidgetCategories.Readiness,
				Icon = "fa-barcode",
				ModuleUrl = "/User/Inventory/Purchasing?tab=Valuation",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 3600,
				Settings = new[] { Number("maxRows", 5, 1, 20) },
				Gates = new DashboardWidgetGates
				{
					Access = new[] { DashboardAccessChecks.InventoryEnabled },
					Role = DashboardRoles.DepartmentAdmin
				}
			};

			// The nav rule IsAdmin || CanViewCertifications: department admins always hold Certifications:View (record claims).
			yield return new DashboardWidgetDefinition
			{
				Key = "certifications.expiry",
				Category = DashboardWidgetCategories.Readiness,
				Icon = "fa-certificate",
				ModuleUrl = "/User/Certifications",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 3600,
				Settings = new[] { Number("maxRows", 5, 1, 20) },
				Gates = new DashboardWidgetGates { Policies = new[] { "Certifications_View" }, Access = new[] { DashboardAccessChecks.CertificationsEnabled } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "certifications.mine",
				Category = DashboardWidgetCategories.Readiness,
				Icon = "fa-certificate",
				ModuleUrl = "/User/Profile/Certifications",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 3600,
				Settings = new[] { Number("maxRows", 5, 1, 20) },
				Gates = new DashboardWidgetGates { Policies = new[] { "Profile_View" }, Access = new[] { DashboardAccessChecks.CertificationsEnabled } }
			};
		}
	}
}
