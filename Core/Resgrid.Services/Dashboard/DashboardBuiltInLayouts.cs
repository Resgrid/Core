using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Dashboard;

namespace Resgrid.Services.Dashboard
{
	/// <summary>
	/// Built-in default boards per audience and the Classic board (home-dashboard-redesign-plan.md §8). Widgets the viewer is
	/// not entitled to are dropped at render time and the dense grid closes the gap, so one default serves every department.
	/// Instance ids are fixed so a member who never saved can still fetch widget data by instance id.
	///
	/// Each default is built from bands that fill all 12 columns at one height (sizes span fixed rows, dashboard.css): four S,
	/// two L, three M, or an L beside two S over two XS. Mixing S (3 wide) and M (4 wide) in one band leaves holes the dense
	/// flow can't close. Add-on widgets come last, so a department without one only loses cards at the end of the board.
	/// </summary>
	public static class DashboardBuiltInLayouts
	{
		private static readonly (string Key, string Size)[] Member =
		{
			("personnel.myStatus", DashboardWidgetSizes.S), ("messages.inbox", DashboardWidgetSizes.S), ("shifts.mine", DashboardWidgetSizes.S),
			("checklists.due", DashboardWidgetSizes.S),
			("calls.active", DashboardWidgetSizes.L), ("calendar.agenda", DashboardWidgetSizes.L),
			("trainings.mine", DashboardWidgetSizes.M), ("certifications.mine", DashboardWidgetSizes.M), ("chat.unread", DashboardWidgetSizes.M),
			("personnel.board", DashboardWidgetSizes.Full)
		};

		private static readonly (string Key, string Size)[] Dispatcher =
		{
			("personnel.myStatus", DashboardWidgetSizes.S), ("calls.kpis", DashboardWidgetSizes.S), ("units.summary", DashboardWidgetSizes.S),
			("personnel.staffingSummary", DashboardWidgetSizes.S),
			("calls.active", DashboardWidgetSizes.L), ("units.status", DashboardWidgetSizes.L),
			("map.operations", DashboardWidgetSizes.L), ("units.alerts", DashboardWidgetSizes.L),
			("calls.awaitingResources", DashboardWidgetSizes.M), ("calls.pending", DashboardWidgetSizes.M), ("weather.alerts", DashboardWidgetSizes.M),
			("personnel.board", DashboardWidgetSizes.Full)
		};

		private static readonly (string Key, string Size)[] Administrator =
		{
			("admin.setup", DashboardWidgetSizes.L), ("personnel.myStatus", DashboardWidgetSizes.S), ("calls.kpis", DashboardWidgetSizes.S),
			("personnel.staffingSummary", DashboardWidgetSizes.Xs), ("units.summary", DashboardWidgetSizes.Xs),
			("calls.active", DashboardWidgetSizes.L), ("units.status", DashboardWidgetSizes.L),
			("certifications.expiry", DashboardWidgetSizes.M), ("checklists.compliance", DashboardWidgetSizes.M), ("admin.plan", DashboardWidgetSizes.M),
			("admin.workflows", DashboardWidgetSizes.M), ("admin.security", DashboardWidgetSizes.M), ("records.overview", DashboardWidgetSizes.M),
			("workOrders.stats", DashboardWidgetSizes.M), ("invoicing.receivables", DashboardWidgetSizes.M), ("ibo.activations", DashboardWidgetSizes.M),
			("personnel.board", DashboardWidgetSizes.Full)
		};

		/// <summary>Today's page on the new engine: board, actions/staffing, department info.</summary>
		private static readonly (string Key, string Size)[] Classic =
		{
			("personnel.board", DashboardWidgetSizes.Xl), ("personnel.myStatus", DashboardWidgetSizes.M), ("department.info", DashboardWidgetSizes.S)
		};

		public static DashboardLayout For(DashboardAudiences audience)
		{
			return audience switch
			{
				DashboardAudiences.Administrator => Build("admin", Administrator),
				DashboardAudiences.Dispatcher => Build("dispatch", Dispatcher),
				_ => Build("member", Member)
			};
		}

		public static DashboardLayout ForClassic() => Build("classic", Classic);

		public static IReadOnlyCollection<string> AllKeys =>
			Member.Concat(Dispatcher).Concat(Administrator).Concat(Classic).Select(w => w.Key).Distinct().ToList();

		private static DashboardLayout Build(string prefix, IEnumerable<(string Key, string Size)> widgets)
		{
			var layout = new DashboardLayout();
			var index = 0;
			foreach (var (key, size) in widgets)
				layout.Widgets.Add(new DashboardLayoutWidget { Id = $"{prefix}-{++index:D2}", Key = key, Size = size });

			return layout;
		}
	}
}
