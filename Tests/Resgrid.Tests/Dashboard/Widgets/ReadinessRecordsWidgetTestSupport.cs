using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Services.Dashboard;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>Contexts and viewers for the readiness and records widget provider tests (agent C slice).</summary>
	internal static class ReadinessRecordsWidgetTestSupport
	{
		internal const int DepartmentId = 41;
		internal const int OtherDepartmentId = 42;
		internal const string UserId = "viewer-1";
		internal const string OtherUserId = "other-2";
		internal const string Grant = "grant-token";

		internal static readonly DateTime Now = new DateTime(2026, 10, 31, 15, 0, 0, DateTimeKind.Utc);

		internal static DashboardWidgetContext Context(string key, IDictionary<string, object> settings = null, string grant = null, string timeZone = null)
		{
			var definition = DashboardWidgetCatalog.Find(key);
			definition.Should().NotBeNull($"{key} is in the catalog");
			return new DashboardWidgetContext
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Viewer = new DashboardViewer { DepartmentId = DepartmentId, UserId = UserId, TimeZoneId = timeZone, AdpEnforced = grant != null },
				Definition = definition,
				Settings = DashboardLayoutValidator.WithDefaults(definition, settings ?? new Dictionary<string, object>()),
				Size = definition.DefaultSize,
				NowUtc = Now,
				ProtectedGrantToken = grant
			};
		}

		/// <summary>A viewer whose snapshot is already complete (IsVisible is pure over it).</summary>
		internal static DashboardViewer Viewer(IEnumerable<string> policies = null, IEnumerable<string> flags = null, IEnumerable<DashboardAccessChecks> access = null,
			IEnumerable<PermissionTypes> permissions = null, bool admin = false)
		{
			var viewer = new DashboardViewer { DepartmentId = DepartmentId, UserId = UserId, IsDepartmentAdmin = admin, IsGroupAdmin = admin };
			foreach (var policy in policies ?? Enumerable.Empty<string>()) viewer.GrantedPolicies.Add(policy);
			foreach (var flag in flags ?? Enumerable.Empty<string>()) viewer.EnabledFlags.Add(flag);
			foreach (var check in Enum.GetValues(typeof(DashboardAccessChecks)).Cast<DashboardAccessChecks>()) viewer.Access[check] = false;
			foreach (var check in access ?? Enumerable.Empty<DashboardAccessChecks>()) viewer.Access[check] = true;
			foreach (var permission in permissions ?? Enumerable.Empty<PermissionTypes>()) viewer.Permissions.Add(permission);
			return viewer;
		}

		/// <summary>The visibility rule only reads the viewer, so the entitlement service is built without its department services.</summary>
		internal static bool Visible(string key, DashboardViewer viewer) =>
			new DashboardEntitlementService(null, null, null, null, null, null, null, null, null, null, null, null, null, null).IsVisible(DashboardWidgetCatalog.Find(key), viewer);

		internal static bool LockedForAdmin(string key, DashboardViewer viewer) =>
			new DashboardEntitlementService(null, null, null, null, null, null, null, null, null, null, null, null, null, null).IsLockedForAdmin(DashboardWidgetCatalog.Find(key), viewer);

		/// <summary>Awaits a provider call that must end in a widget state, never a raw module message.</summary>
		internal static async Task<DashboardWidgetStateException> StateAsync(Func<Task> call)
		{
			DashboardWidgetStateException state = null;
			try { await call(); }
			catch (DashboardWidgetStateException ex) { state = ex; }
			state.Should().NotBeNull("the module failure maps to a widget state");
			state.Message.Should().NotContain("Unlock").And.NotContain("ProtectedDataRequired");
			return state;
		}
	}
}
