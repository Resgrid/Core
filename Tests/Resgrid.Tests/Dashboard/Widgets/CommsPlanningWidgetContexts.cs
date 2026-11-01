using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Services.Dashboard;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>Builds provider contexts for the awareness, communications and planning widget tests the way DashboardWidgetDataService does.</summary>
	internal static class CommsPlanningWidgetContexts
	{
		public const int DepartmentId = 42;
		public const int OtherDepartmentId = 77;
		public const string UserId = "user-1";

		public static readonly DateTime Now = new DateTime(2026, 10, 31, 15, 0, 0, DateTimeKind.Utc);

		public static DashboardWidgetContext For(string key, IDictionary<string, object> settings = null, bool isAdmin = false, bool adpEnforced = false,
			IEnumerable<PermissionTypes> adpPermissions = null, string grant = null, string timeZone = "Pacific Standard Time", IEnumerable<int> roleIds = null)
		{
			var definition = DashboardWidgetCatalog.Find(key) ?? throw new ArgumentException("Unknown widget " + key);
			var viewer = new DashboardViewer
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				IsDepartmentAdmin = isAdmin,
				IsGroupAdmin = isAdmin,
				AdpEnforced = adpEnforced,
				AdpViewPermissions = new HashSet<PermissionTypes>(adpPermissions ?? Enumerable.Empty<PermissionTypes>()),
				TimeZoneId = timeZone,
				PersonnelRoleIds = (roleIds ?? Enumerable.Empty<int>()).ToList()
			};

			return new DashboardWidgetContext
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Viewer = viewer,
				Definition = definition,
				Settings = DashboardLayoutValidator.WithDefaults(definition, settings ?? new Dictionary<string, object>()),
				Size = definition.DefaultSize,
				NowUtc = Now,
				ProtectedGrantToken = adpEnforced ? grant : null
			};
		}

		public static Dictionary<string, object> Settings(params (string Name, object Value)[] values) =>
			values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
	}
}
