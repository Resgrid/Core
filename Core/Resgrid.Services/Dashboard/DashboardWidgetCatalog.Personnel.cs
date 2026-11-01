using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Dashboard;

namespace Resgrid.Services.Dashboard
{
	public static partial class DashboardWidgetCatalog
	{
		/// <summary>
		/// Personnel widgets (home-dashboard-redesign-plan.md §7.3). My Status and the status board keep today's dashboard rule
		/// (Department_View); the summaries follow the Personnel module (Personnel_View).
		/// </summary>
		private static partial IEnumerable<DashboardWidgetDefinition> PersonnelWidgets()
		{
			yield return new DashboardWidgetDefinition
			{
				Key = "personnel.myStatus",
				Category = DashboardWidgetCategories.Personnel,
				Icon = "fa-user",
				ModuleUrl = "/User/Personnel/Index",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				RealtimeTopics = Topics(DashboardTopics.PersonnelStatus, DashboardTopics.PersonnelStaffing),
				DefaultRefreshSeconds = 60,
				Gates = new DashboardWidgetGates { Policies = new[] { "Department_View" } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "personnel.board",
				Category = DashboardWidgetCategories.Personnel,
				Icon = "fa-users",
				ModuleUrl = "/User/Personnel/Index",
				AllowedSizes = Sizes(DashboardWidgetSizes.L, DashboardWidgetSizes.Xl, DashboardWidgetSizes.Full),
				DefaultSize = DashboardWidgetSizes.Full,
				RealtimeTopics = Topics(DashboardTopics.PersonnelStatus, DashboardTopics.PersonnelStaffing),
				DefaultRefreshSeconds = 60,
				Settings = new[]
				{
					MultiFrom("groups", DashboardOptionSources.Groups),
					Toggle("hideUnavailable"),
					Select("sort", "department", "department", "name", "status")
				},
				Gates = new DashboardWidgetGates { Policies = new[] { "Department_View" } },
				ProtectedFields = new[] { new DashboardProtectedField { FieldId = "userstates.note", ViewPermission = PermissionTypes.ViewProtectedOperationalData } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "personnel.statusSummary",
				Category = DashboardWidgetCategories.Personnel,
				Icon = "fa-user",
				ModuleUrl = "/User/Personnel/Index",
				AllowedSizes = Sizes(DashboardWidgetSizes.Xs, DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				RealtimeTopics = Topics(DashboardTopics.PersonnelStatus),
				DefaultRefreshSeconds = 60,
				Gates = new DashboardWidgetGates { Policies = new[] { "Personnel_View" } }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "personnel.staffingSummary",
				Category = DashboardWidgetCategories.Personnel,
				Icon = "fa-user",
				ModuleUrl = "/User/Personnel/Index",
				AllowedSizes = Sizes(DashboardWidgetSizes.Xs, DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				RealtimeTopics = Topics(DashboardTopics.PersonnelStaffing),
				DefaultRefreshSeconds = 60,
				Gates = new DashboardWidgetGates { Policies = new[] { "Personnel_View" } }
			};
		}
	}
}
