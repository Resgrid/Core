using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Shared evaluation for the "who may act in this capacity" permissions — dispatch and command.
	///
	/// Both answer the same question against a different <see cref="PermissionTypes"/> value, and both
	/// gate access to private traffic, so the rules that matter live in one place: a missing permission
	/// row means everyone, the department's managing user counts as an admin, and an evaluation failure
	/// denies rather than allows.
	/// </summary>
	public abstract class PermissionGateServiceBase
	{
		private static readonly TimeSpan CacheLength = TimeSpan.FromSeconds(60);

		private readonly IPermissionsService _permissionsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly ICacheProvider _cacheProvider;

		protected PermissionGateServiceBase(
			IPermissionsService permissionsService,
			IDepartmentsService departmentsService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			ICacheProvider cacheProvider)
		{
			_permissionsService = permissionsService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_cacheProvider = cacheProvider;
		}

		/// <summary>The permission this gate evaluates.</summary>
		protected abstract PermissionTypes PermissionType { get; }

		/// <summary>Cache key prefix, unique per gate so the two never share a verdict.</summary>
		protected abstract string CacheKeyPrefix { get; }

		protected async Task<bool> IsAllowedAsync(int departmentId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return false;

			var cacheKey = $"{CacheKeyPrefix}:{departmentId}:{userId}";

			try
			{
				var cached = await _cacheProvider.GetStringAsync(cacheKey);
				if (cached == "1")
					return true;
				if (cached == "0")
					return false;
			}
			catch (Exception ex)
			{
				// A cache outage must not lock people out — fall through and evaluate.
				Logging.LogException(ex);
			}

			var allowed = await EvaluateAsync(departmentId, userId);

			try
			{
				await _cacheProvider.SetStringAsync(cacheKey, allowed ? "1" : "0", CacheLength);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return allowed;
		}

		/// <summary>
		/// True when the department has deliberately narrowed this permission — a row exists and it is not
		/// "Everyone".
		///
		/// Used where granting a capability off the back of the OPEN default would be a surprise: with no
		/// row, or a row that admits everyone, the department has expressed no opinion about who is
		/// trusted, so nothing extra should be inferred from it.
		/// </summary>
		protected async Task<bool> IsRestrictedAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;

			try
			{
				var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionType);
				return permission != null && permission.Action != (int)PermissionActions.Everyone;
			}
			catch (Exception ex)
			{
				// Unreadable means "no opinion expressed", which grants nothing extra.
				Logging.LogException(ex);
				return false;
			}
		}

		protected async Task<List<string>> GetAllowedUserIdsAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new List<string>();

			var members = await _departmentsService.GetAllMembersForDepartmentAsync(departmentId) ?? new List<DepartmentMember>();
			var active = members
				.Where(m => !m.IsDisabled.GetValueOrDefault() && !m.IsDeleted && !string.IsNullOrWhiteSpace(m.UserId))
				.ToList();

			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionType);

			// The overwhelmingly common case — no permission row, or one that allows everyone — needs no
			// per-user evaluation at all. Only a genuinely restricted department pays for the fan-out.
			if (permission == null || permission.Action == (int)PermissionActions.Everyone)
				return active.Select(m => m.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			var allowed = new List<string>();
			foreach (var member in active)
			{
				if (await IsAllowedAsync(departmentId, member.UserId))
					allowed.Add(member.UserId);
			}

			return allowed.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>
		/// Mirrors how the department rights endpoint decides every other permission: department admin,
		/// group admin, and the user's personnel roles evaluated against the permission row. A missing row
		/// means everyone, which <see cref="IPermissionsService.IsUserAllowed"/> already handles.
		/// </summary>
		private async Task<bool> EvaluateAsync(int departmentId, string userId)
		{
			try
			{
				var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionType);
				if (permission == null)
					return true;

				var membership = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId, false);
				if (membership == null)
					return false;

				var isDepartmentAdmin = membership.IsAdmin.GetValueOrDefault();

				// The department's managing user is always an admin, the same carve-out the rights endpoint makes.
				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
				if (department != null && string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase))
					isDepartmentAdmin = true;

				var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
				var isGroupAdmin = group != null && group.IsUserGroupAdmin(userId);

				var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);

				return _permissionsService.IsUserAllowed(permission, isDepartmentAdmin, isGroupAdmin, roles);
			}
			catch (Exception ex)
			{
				// Fail CLOSED. These gates exist to keep private command, unit, responder and dispatch
				// traffic away from people the department hasn't authorized; an error must not hand it over.
				Logging.LogException(ex);
				return false;
			}
		}
	}
}
