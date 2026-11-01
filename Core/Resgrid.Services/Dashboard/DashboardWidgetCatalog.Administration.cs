using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Dashboard;

namespace Resgrid.Services.Dashboard
{
	public static partial class DashboardWidgetCatalog
	{
		/// <summary>Administration widgets (home-dashboard-redesign-plan.md §7.11).</summary>
		private static partial IEnumerable<DashboardWidgetDefinition> AdministrationWidgets()
		{
			yield return new DashboardWidgetDefinition
			{
				Key = "department.info",
				Category = DashboardWidgetCategories.Administration,
				Icon = "fa-info-circle",
				ModuleUrl = "/User/Department/Settings",
				AllowedSizes = Sizes(DashboardWidgetSizes.Xs, DashboardWidgetSizes.S),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 3600,
				Gates = new DashboardWidgetGates { Policies = new[] { "Department_View" } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "admin.setup",
				Category = DashboardWidgetCategories.Administration,
				Icon = "fa-magic",
				ModuleUrl = "/User/AdminAssist/SetupWizard",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 3600,
				Gates = new DashboardWidgetGates
				{
					Role = DashboardRoles.DepartmentAdmin,
					Access = new[] { DashboardAccessChecks.AdminAssistAvailable }
				}
			};

			// The Subscription page: Department_Update, department admins; billing figures only for a subscription manager (provider).
			yield return new DashboardWidgetDefinition
			{
				Key = "admin.plan",
				Category = DashboardWidgetCategories.Administration,
				Icon = "fa-credit-card",
				ModuleUrl = "/User/Subscription",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 3600,
				Gates = new DashboardWidgetGates { Role = DashboardRoles.DepartmentAdmin, Policies = new[] { "Department_Update" } }
			};

			// Workflows list (Workflow_View) and Pending / Health (WorkflowRun_View); the CreateWorkflow / ViewWorkflowRuns permission
			// rows are re-checked by the provider. No ProtectedFields: run error text is never read, so the widget has nothing to reveal
			// and must not raise the "Verify to reveal" chip (plan §9A.2).
			yield return new DashboardWidgetDefinition
			{
				Key = "admin.workflows",
				Category = DashboardWidgetCategories.Administration,
				Icon = "fa-cogs",
				ModuleUrl = "/User/Workflows",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 300,
				Settings = new[] { Number("maxRows", 5, 1, 20) },
				Gates = new DashboardWidgetGates { Policies = new[] { "Workflow_View", "WorkflowRun_View" } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "admin.dataProtection",
				Category = DashboardWidgetCategories.Administration,
				Icon = "fa-shield",
				ModuleUrl = "/User/DataProtection",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 3600,
				Gates = new DashboardWidgetGates
				{
					Role = DashboardRoles.DepartmentAdmin,
					Access = new[] { DashboardAccessChecks.DataProtectionOffered },
					Addon = PlanAddonTypes.ADP
				}
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "admin.import",
				Category = DashboardWidgetCategories.Administration,
				Icon = "fa-upload",
				ModuleUrl = "/User/DepartmentImport",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 300,
				Gates = new DashboardWidgetGates { Role = DashboardRoles.DepartmentAdmin, Flags = new[] { FeatureFlagKeys.OnboardingMigrationWizard } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "admin.security",
				Category = DashboardWidgetCategories.Administration,
				Icon = "fa-lock",
				ModuleUrl = "/User/Security",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 3600,
				Gates = new DashboardWidgetGates { Role = DashboardRoles.DepartmentAdmin }
			};
		}
	}
}
