using System.Collections.Generic;
using Resgrid.Model.Dashboard;
using Resgrid.Services.Dashboard.Widgets.Planning;

namespace Resgrid.Services.Dashboard
{
	public static partial class DashboardWidgetCatalog
	{
		/// <summary>Planning widgets (home-dashboard-redesign-plan.md §7.6).</summary>
		private static partial IEnumerable<DashboardWidgetDefinition> PlanningWidgets()
		{
			yield return new DashboardWidgetDefinition
			{
				Key = "calendar.agenda",
				Category = DashboardWidgetCategories.Planning,
				Icon = "fa-calendar",
				ModuleUrl = "/User/Calendar",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 300,
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Schedule_View" },
					Access = new[] { DashboardAccessChecks.CalendarEnabled }
				},
				Settings = new[]
				{
					Number("days", 7, 1, 14),
					MultiFrom("types", PlanningOptionSources.CalendarTypes),
					Number("maxItems", 10, 1, 25)
				}
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "shifts.today",
				Category = DashboardWidgetCategories.Planning,
				Icon = "fa-briefcase",
				ModuleUrl = "/User/Shifts/OnDuty",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 300,
				Gates = ShiftGates()
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "shifts.mine",
				Category = DashboardWidgetCategories.Planning,
				Icon = "fa-briefcase",
				ModuleUrl = "/User/Shifts/YourShifts",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 300,
				Gates = ShiftGates(),
				Settings = new[]
				{
					Number("days", 30, 1, 90),
					Number("maxItems", 5, 1, 20)
				}
			};

			// Approver-only content (supervisors and setting-121 trade approver roles); a member who approves nothing gets
			// HasApprovals = false, as the Approvals page refuses them.
			yield return new DashboardWidgetDefinition
			{
				Key = "shifts.approvals",
				Category = DashboardWidgetCategories.Planning,
				Icon = "fa-check-circle",
				ModuleUrl = "/User/Shifts/Approvals",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 300,
				Gates = ShiftGates(),
				Settings = new[] { Number("maxItems", 5, 1, 20) }
			};

			// Supervisor-only content (the Shift Staffing page rule); others get CanView = false.
			yield return new DashboardWidgetDefinition
			{
				Key = "shifts.coverage",
				Category = DashboardWidgetCategories.Planning,
				Icon = "fa-users",
				ModuleUrl = "/User/Shifts/ShiftStaffing",
				AllowedSizes = Sizes(DashboardWidgetSizes.L, DashboardWidgetSizes.Xl),
				DefaultSize = DashboardWidgetSizes.L,
				DefaultRefreshSeconds = 900,
				Gates = ShiftGates(),
				Settings = new[]
				{
					Number("days", 7, 1, 14),
					Toggle("gapsOnly", false)
				}
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "trainings.mine",
				Category = DashboardWidgetCategories.Planning,
				Icon = "fa-book",
				ModuleUrl = "/User/Trainings",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 900,
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Training_View" },
					Access = new[] { DashboardAccessChecks.TrainingEnabled }
				},
				Settings = new[] { Number("maxItems", 5, 1, 20) }
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "documents.recent",
				Category = DashboardWidgetCategories.Planning,
				Icon = "fa-file-archive-o",
				ModuleUrl = "/User/Documents",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 900,
				Gates = new DashboardWidgetGates
				{
					Policies = new[] { "Documents_View" },
					Access = new[] { DashboardAccessChecks.DocumentsEnabled }
				},
				Settings = new[]
				{
					SelectFrom("category", PlanningOptionSources.DocumentCategories),
					Number("maxItems", 8, 1, 25)
				}
			};

			// The Notes index carries no policy beyond [Authorize]; the module switch is the gate.
			yield return new DashboardWidgetDefinition
			{
				Key = "notes.board",
				Category = DashboardWidgetCategories.Planning,
				Icon = "fa-clipboard",
				ModuleUrl = "/User/Notes",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 300,
				Gates = new DashboardWidgetGates { Access = new[] { DashboardAccessChecks.NotesEnabled } },
				Settings = new[]
				{
					SelectFrom("category", PlanningOptionSources.NoteCategories),
					Number("maxItems", 6, 1, 20),
					Toggle("hideExpired", true)
				}
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "contacts.preplans",
				Category = DashboardWidgetCategories.Planning,
				Icon = "fa-address-book",
				ModuleUrl = "/User/Contacts",
				AllowedSizes = Sizes(DashboardWidgetSizes.M, DashboardWidgetSizes.L),
				DefaultSize = DashboardWidgetSizes.M,
				DefaultRefreshSeconds = 900,
				Gates = new DashboardWidgetGates { Policies = new[] { "Contacts_View" }, Access = new[] { DashboardAccessChecks.ContactsEnabled } },
				Settings = new[] { Number("maxItems", 8, 1, 25) },
				ProtectedFields = new[]
				{
					new DashboardProtectedField { FieldId = "contacts.firstname", ViewPermission = Resgrid.Model.PermissionTypes.ViewProtectedContactData },
					new DashboardProtectedField { FieldId = "contacts.lastname", ViewPermission = Resgrid.Model.PermissionTypes.ViewProtectedContactData }
				}
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "links.quick",
				Category = DashboardWidgetCategories.Planning,
				Icon = "fa-link",
				ModuleUrl = "/User/Links",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 3600,
				Settings = new[]
				{
					Toggle("showDepartmentLinks", true),
					Text("links", Widgets.Planning.QuickLinksWidgetProvider.MaxLinksSettingLength)
				}
			};

			yield return new DashboardWidgetDefinition
			{
				Key = "general.stickyNote",
				Category = DashboardWidgetCategories.Planning,
				Icon = "fa-sticky-note-o",
				// No module of its own (the note lives in the widget settings); home is always reachable.
				ModuleUrl = "/User/Home/Dashboard",
				AllowedSizes = Sizes(DashboardWidgetSizes.S, DashboardWidgetSizes.M),
				DefaultSize = DashboardWidgetSizes.S,
				DefaultRefreshSeconds = 3600,
				Settings = new[] { Text("text", Widgets.Planning.StickyNoteWidgetProvider.MaxTextLength) },
				// A member may keep several notes (plan mockup).
				SupportsMultipleInstances = true
			};
		}

		private static DashboardWidgetGates ShiftGates() => new DashboardWidgetGates
		{
			Policies = new[] { "Shift_View" },
			Access = new[] { DashboardAccessChecks.ShiftsEnabled }
		};
	}
}
