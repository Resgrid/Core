using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Services.Wellness
{
	/// <summary>
	/// Who acts in each wellness role (<see cref="WellnessRoleRules"/>, decided 2026-11-26), shared by the WL0 and WL1 services so the
	/// officer checks, the report and notice recipients and the roster never drift. Role membership decides; with
	/// <see cref="WellnessSettings.RequireCurrentCertification"/> on, a role member also needs a current certification of the role's linked
	/// type. Built by each service from its own injected dependencies; nothing here is registered in the container.
	/// </summary>
	internal sealed class WellnessRoleResolver
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IPersonnelRolesService _rolesService;
		private readonly Lazy<ICertificationService> _certificationService;

		public WellnessRoleResolver(IDepartmentsService departmentsService, IPersonnelRolesService rolesService, Lazy<ICertificationService> certificationService)
		{
			_departmentsService = departmentsService;
			_rolesService = rolesService;
			_certificationService = certificationService;
		}

		/// <summary>The department-local date certifications are compared on.</summary>
		public async Task<DateTime> LocalTodayAsync(int departmentId)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			return department == null ? DateTime.UtcNow.Date : DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc).TimeConverter(department).Date;
		}

		/// <summary>The department's own, person-scoped, not deleted type; null for anything else.</summary>
		public async Task<DepartmentCertificationType> TypeAsync(int departmentId, int typeId)
		{
			if (typeId <= 0)
				return null;
			var type = await _certificationService.Value.GetCertificationTypeByIdAsync(typeId);
			return type == null || type.DepartmentId != departmentId || type.IsDeleted || type.AppliesTo != (int)CertificationAppliesTo.Person ? null : type;
		}

		/// <summary>
		/// Whether a member who holds the role of <paramref name="kind"/> also passes its certification rule (always true while the
		/// switch is off or the role has no linked type). Never throws: a failed read keeps the role-only answer and is logged.
		/// </summary>
		public async Task<bool> PassesCertificationAsync(int departmentId, WellnessSettings settings, WellnessRoleKinds kind, string userId)
		{
			if (!WellnessRoleRules.RequiresCertification(settings, kind))
				return true;
			try
			{
				var type = await TypeAsync(departmentId, WellnessRoleRules.CertificationTypeIdFor(settings, kind));
				if (type == null)
					return WellnessRoleRules.Acts(settings, kind, new WellnessCertificationStatus { State = WellnessCertificationStates.NotLinked });
				var records = await _certificationService.Value.GetCertificationsForDepartmentAsync(departmentId, new[] { userId }) ?? new List<PersonnelCertification>();
				var status = WellnessRoleRules.Status(records.Where(r => string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase)), type, await LocalTodayAsync(departmentId));
				return WellnessRoleRules.Acts(settings, kind, status);
			}
			catch (Exception ex)
			{
				// Fail closed: with the switch on, a member whose certification cannot be read does not act until it can.
				Logging.LogException(ex, $"Wellness certification check failed for department {departmentId}.");
				return false;
			}
		}

		/// <summary>Whether <paramref name="userId"/> acts in <paramref name="kind"/>, given the role ids the member holds.</summary>
		public async Task<bool> ActsAsync(int departmentId, WellnessSettings settings, WellnessRoleKinds kind, string userId, ICollection<int> heldRoleIds)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return false;
			if (kind == WellnessRoleKinds.PeerSupportTeam && WellnessRoleRules.PeerTeamByCertificationOnly(settings))
				return (await ActingMembersAsync(departmentId, settings, kind)).Contains(userId, StringComparer.OrdinalIgnoreCase);

			var roleId = WellnessRoleRules.RoleIdFor(settings, kind);
			if (roleId <= 0 || heldRoleIds == null || !heldRoleIds.Contains(roleId))
				return false;
			return await PassesCertificationAsync(departmentId, settings, kind, userId);
		}

		/// <summary>The active members acting in <paramref name="kind"/> (case-insensitive, distinct).</summary>
		public async Task<List<string>> ActingMembersAsync(int departmentId, WellnessSettings settings, WellnessRoleKinds kind)
		{
			var roster = await RosterAsync(departmentId, settings, kind);
			return roster.Members.Where(m => m.Acts).Select(m => m.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>Every wellness role with its members, their certification status and whether they act.</summary>
		public async Task<List<WellnessRoleRoster>> RostersAsync(int departmentId, WellnessSettings settings)
		{
			var rosters = new List<WellnessRoleRoster>();
			foreach (var kind in WellnessRoleRules.Kinds)
				rosters.Add(await RosterAsync(departmentId, settings, kind));
			return rosters;
		}

		/// <summary>One wellness role: its members (active department members only), status and whether each acts.</summary>
		public async Task<WellnessRoleRoster> RosterAsync(int departmentId, WellnessSettings settings, WellnessRoleKinds kind)
		{
			settings = (settings ?? new WellnessSettings()).Normalized();
			var roster = new WellnessRoleRoster
			{
				Kind = kind,
				RoleId = WellnessRoleRules.RoleIdFor(settings, kind),
				RoleFallsBack = kind == WellnessRoleKinds.DesignatedOfficer && settings.DesignatedOfficerRoleId <= 0 && settings.WellnessOfficerRoleId > 0,
				CertificationTypeId = WellnessRoleRules.CertificationTypeIdFor(settings, kind),
				RequiresCertification = WellnessRoleRules.RequiresCertification(settings, kind),
				ByCertificationOnly = kind == WellnessRoleKinds.PeerSupportTeam && WellnessRoleRules.PeerTeamByCertificationOnly(settings)
			};

			var type = await TypeAsync(departmentId, roster.CertificationTypeId);
			roster.CertificationTypeName = type?.Type;
			roster.CertificationTypeMissing = roster.CertificationTypeId > 0 && type == null;
			var active = await _departmentsService.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			List<string> userIds;
			if (roster.ByCertificationOnly)
			{
				// The team is whoever holds a valid certification of the type (the certification module's own rule, as before the role).
				userIds = type == null || string.IsNullOrWhiteSpace(type.Code)
					? new List<string>()
					: await _certificationService.Value.GetUsersWithValidCertificationAsync(departmentId, new[] { type.Code }) ?? new List<string>();
			}
			else if (roster.RoleId > 0)
			{
				userIds = (await _rolesService.GetAllMembersOfRoleAsync(roster.RoleId) ?? new List<PersonnelRoleUser>())
					.Where(m => m != null)
					.Select(m => m.UserId)
					.ToList();
			}
			else
			{
				return roster;
			}

			userIds = userIds.Where(u => !string.IsNullOrWhiteSpace(u) && active.Contains(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (userIds.Count == 0)
				return roster;

			var onDate = await LocalTodayAsync(departmentId);
			var records = type == null
				? new List<PersonnelCertification>()
				: await _certificationService.Value.GetCertificationsForDepartmentAsync(departmentId, userIds) ?? new List<PersonnelCertification>();
			foreach (var userId in userIds)
			{
				var status = WellnessRoleRules.Status(records.Where(r => string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase)), type, onDate);
				roster.Members.Add(new WellnessRoleRosterMember
				{
					UserId = userId,
					Certification = status,
					// A holder-only team is made of current holders by definition.
					Acts = roster.ByCertificationOnly || WellnessRoleRules.Acts(settings, kind, status)
				});
			}

			return roster;
		}
	}
}
