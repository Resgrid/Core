using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets
{
	/// <summary>
	/// Evaluates a module permission exactly as the sign-in claims are derived (ClaimsLogic.AddRecordClaims →
	/// RecordPermissionEvaluation): the department's Permission row, or the catalog's no-row default, against the viewer's admin
	/// flag, group-admin flag and roles. Providers use it for a module page's secondary rule (for example "managers see every
	/// deployment, members the ones they are rostered on"), which the page checks with a claim inside the action rather than with
	/// its index policy, so it cannot be a widget gate. Unknown types fail closed.
	/// </summary>
	internal static class DashboardModulePermissions
	{
		private static readonly IReadOnlyList<RecordPermissionDescriptor> Catalog = DeploymentPermissionCatalog.All
			.Concat(InvoicingPermissionCatalog.All)
			.Concat(WorkforcePermissionCatalog.All)
			.Concat(IncidentBackOfficePermissionCatalog.All)
			.ToList();

		/// <summary>True when any of <paramref name="types"/> issues the claim the page checks. One permissions read.</summary>
		public static async Task<bool> HoldsAnyAsync(IPermissionsService permissions, DashboardViewer viewer, params PermissionTypes[] types) =>
			(await HoldsEachAsync(permissions, viewer, types))[0];

		/// <summary>
		/// One answer per claim, each claim given as the permission types that issue it (a claim several permissions grant, such as
		/// IncidentBudget:View, passes when any of them does). One permissions read for all of them.
		/// </summary>
		public static async Task<bool[]> HoldsEachAsync(IPermissionsService permissions, DashboardViewer viewer, params PermissionTypes[][] claims)
		{
			var result = new bool[claims?.Length ?? 0];
			if (viewer == null || result.Length == 0)
				return result;

			// Department administrators pass every value of the ladder.
			if (viewer.IsDepartmentAdmin)
				return result.Select(_ => true).ToArray();

			var rows = await permissions.GetAllPermissionsForDepartmentAsync(viewer.DepartmentId) ?? new List<Permission>();
			var roles = viewer.PersonnelRoleIds.Select(id => new PersonnelRole { PersonnelRoleId = id }).ToList();

			for (var i = 0; i < claims.Length; i++)
				result[i] = (claims[i] ?? new PermissionTypes[0]).Any(type => Satisfied(type, rows, viewer, roles));

			return result;
		}

		private static bool Satisfied(PermissionTypes type, List<Permission> rows, DashboardViewer viewer, List<PersonnelRole> roles)
		{
			var descriptor = Catalog.FirstOrDefault(d => d.Type == type);
			if (descriptor == null)
				return false;

			var row = rows.FirstOrDefault(p => p.PermissionType == (int)type);
			return RecordPermissionEvaluation.IsSatisfied(row?.Action ?? (int)descriptor.NoRowDefault, row?.Data, false, viewer.IsGroupAdmin, roles);
		}
	}
}
