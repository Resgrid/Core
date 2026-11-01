using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>Builders for the business, Incident Back Office, intelligence and administration widget tests.</summary>
	internal static class AgentDWidgetTestSupport
	{
		public const int Dept = 52;
		public const int OtherDept = 77;
		public const string User = "user-52";

		public static readonly DateTime Now = new DateTime(2026, 10, 31, 15, 0, 0, DateTimeKind.Utc);

		public static DashboardViewer Viewer(bool admin = false, bool groupAdmin = false, IEnumerable<string> policies = null, IEnumerable<string> flags = null,
			IEnumerable<DashboardAccessChecks> access = null, IEnumerable<int> roles = null, bool adpEnforced = false, string timeZone = null)
		{
			var viewer = new DashboardViewer
			{
				DepartmentId = Dept,
				UserId = User,
				IsDepartmentAdmin = admin,
				IsGroupAdmin = admin || groupAdmin,
				PersonnelRoleIds = (roles ?? Enumerable.Empty<int>()).ToList(),
				AdpEnforced = adpEnforced,
				TimeZoneId = timeZone
			};
			foreach (var policy in policies ?? Enumerable.Empty<string>()) viewer.GrantedPolicies.Add(policy);
			foreach (var flag in flags ?? Enumerable.Empty<string>()) viewer.EnabledFlags.Add(flag);
			foreach (var check in Enum.GetValues(typeof(DashboardAccessChecks)).Cast<DashboardAccessChecks>()) viewer.Access[check] = false;
			foreach (var check in access ?? Enumerable.Empty<DashboardAccessChecks>()) viewer.Access[check] = true;
			return viewer;
		}

		/// <summary>A provider context with settings validated and defaulted exactly as DashboardWidgetDataService does it.</summary>
		public static DashboardWidgetContext Context(string key, DashboardViewer viewer = null, IDictionary<string, object> settings = null)
		{
			var definition = DashboardWidgetCatalog.Find(key) ?? throw new InvalidOperationException($"{key} is not in the catalog");
			viewer ??= Viewer();
			return new DashboardWidgetContext
			{
				DepartmentId = viewer.DepartmentId,
				UserId = viewer.UserId,
				Viewer = viewer,
				Definition = definition,
				Settings = DashboardLayoutValidator.WithDefaults(definition, settings ?? new Dictionary<string, object>()),
				Size = definition.DefaultSize,
				NowUtc = Now
			};
		}

		public static bool Visible(string key, DashboardViewer viewer) =>
			new DashboardEntitlementService(null, null, null, null, null, null, null, null, null, null, null, null, null, null).IsVisible(DashboardWidgetCatalog.Find(key), viewer);

		public static bool LockedForAdmin(string key, DashboardViewer viewer) =>
			new DashboardEntitlementService(null, null, null, null, null, null, null, null, null, null, null, null, null, null).IsLockedForAdmin(DashboardWidgetCatalog.Find(key), viewer);

		/// <summary>The department's permission rows for the claim-equivalent checks (none = every catalog no-row default applies).</summary>
		public static Mock<IPermissionsService> Permissions(params Permission[] rows)
		{
			var permissions = new Mock<IPermissionsService>(MockBehavior.Strict);
			permissions.Setup(p => p.GetAllPermissionsForDepartmentAsync(Dept)).ReturnsAsync(rows.ToList());
			return permissions;
		}

		public static Permission Row(PermissionTypes type, PermissionActions action, string roleCsv = null) =>
			new Permission { DepartmentId = Dept, PermissionType = (int)type, Action = (int)action, Data = roleCsv };

		public static async Task<DashboardWidgetStateException> StateAsync(Func<Task> call)
		{
			DashboardWidgetStateException state = null;
			try { await call(); }
			catch (DashboardWidgetStateException ex) { state = ex; }
			state.Should().NotBeNull("the provider ends in a widget state");
			return state;
		}
	}
}
