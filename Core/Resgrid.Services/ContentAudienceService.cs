using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Resolves <see cref="ContentAudienceKinds"/> rules to active members (shared by Training T0 and Protocols P1). Group,
	/// role, certification type, unit type and shift ids are global keys, so each is checked to belong to the department
	/// before its members count. Only active members come back (inactive-member policy).
	/// </summary>
	public class ContentAudienceService : IContentAudienceService
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly ICertificationService _certificationService;
		private readonly IUnitsService _unitsService;
		private readonly IShiftsService _shiftsService;

		public ContentAudienceService(IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService, IPersonnelRolesService personnelRolesService,
			ICertificationService certificationService, IUnitsService unitsService, IShiftsService shiftsService)
		{
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_certificationService = certificationService;
			_unitsService = unitsService;
			_shiftsService = shiftsService;
		}

		/// <summary>A certification record that puts its holder in a certification-type audience.</summary>
		public static bool IsCurrentCertification(PersonnelCertification certification, int departmentId, DateTime utcNow)
		{
			return certification != null && certification.DepartmentId == departmentId && !certification.IsDeleted && certification.DepartmentCertificationTypeId.HasValue &&
				(certification.Status == (int)PersonnelCertificationStatuses.Active || certification.Status == (int)PersonnelCertificationStatuses.Trainee) &&
				(!certification.ExpiresOn.HasValue || certification.ExpiresOn.Value >= utcNow);
		}

		public async Task<ContentAudienceResolution> ResolveAsync(int departmentId, IEnumerable<IContentAudienceRule> rules)
		{
			var result = new ContentAudienceResolution();
			var normalized = ContentAudienceMatcher.Normalize(rules);
			if (departmentId <= 0 || normalized.Count == 0)
				return result;

			var active = await _departmentsService.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			List<PersonnelCertification> certifications = null;
			List<UnitActiveRole> activeRoles = null;
			List<Unit> units = null;
			var now = DateTime.UtcNow;

			foreach (var rule in normalized)
			{
				switch ((ContentAudienceKinds)rule.Kind)
				{
					case ContentAudienceKinds.Everyone:
						ids.UnionWith(active);
						break;
					case ContentAudienceKinds.User:
						ids.Add(rule.RefId);
						break;
					case ContentAudienceKinds.Group:
						if (ContentAudienceMatcher.TryId(rule, out var groupId) && (await _departmentGroupsService.GetGroupByIdAsync(groupId, false))?.DepartmentId == departmentId)
							ids.UnionWith((await _departmentGroupsService.GetAllMembersForGroupAsync(groupId) ?? new List<DepartmentGroupMember>()).Where(m => m != null).Select(m => m.UserId));
						break;
					case ContentAudienceKinds.Role:
						if (ContentAudienceMatcher.TryId(rule, out var roleId) && (await _personnelRolesService.GetRoleByIdAsync(roleId))?.DepartmentId == departmentId)
							ids.UnionWith((await _personnelRolesService.GetAllMembersOfRoleAsync(roleId) ?? new List<PersonnelRoleUser>()).Where(m => m != null).Select(m => m.UserId));
						break;
					case ContentAudienceKinds.CertificationType:
						if (ContentAudienceMatcher.TryId(rule, out var typeId))
						{
							certifications ??= await _certificationService.GetCertificationsForDepartmentAsync(departmentId) ?? new List<PersonnelCertification>();
							ids.UnionWith(certifications.Where(c => c.DepartmentCertificationTypeId == typeId && IsCurrentCertification(c, departmentId, now)).Select(c => c.UserId));
						}
						break;
					case ContentAudienceKinds.UnitType:
						if (ContentAudienceMatcher.TryId(rule, out var unitTypeId))
						{
							var unitType = await _unitsService.GetUnitTypeByIdAsync(unitTypeId);
							if (unitType == null || unitType.DepartmentId != departmentId)
								break;
							units ??= await _unitsService.GetUnitsForDepartmentAsync(departmentId) ?? new List<Unit>();
							activeRoles ??= await _unitsService.GetAllActiveRolesForUnitsByDepartmentIdAsync(departmentId) ?? new List<UnitActiveRole>();
							var unitIds = new HashSet<int>(units.Where(u => string.Equals(u.Type, unitType.Type, StringComparison.OrdinalIgnoreCase)).Select(u => u.UnitId));
							ids.UnionWith(activeRoles.Where(r => unitIds.Contains(r.UnitId)).Select(r => r.UserId));
						}
						break;
					case ContentAudienceKinds.Shift:
						if (ContentAudienceMatcher.TryId(rule, out var shiftId))
						{
							var shift = await _shiftsService.GetShiftByIdAsync(shiftId);
							if (shift == null || shift.DepartmentId != departmentId)
								break;
							shift = await _shiftsService.PopulateShiftData(shift, false, true, false, false, false);
							ids.UnionWith((shift?.Personnel ?? new List<ShiftPerson>()).Where(p => p != null).Select(p => p.UserId));
						}
						break;
				}
			}

			ids.RemoveWhere(string.IsNullOrWhiteSpace);
			result.MemberUserIds = new HashSet<string>(ids.Where(active.Contains), StringComparer.OrdinalIgnoreCase);

			var picked = normalized.Where(r => r.Kind == (int)ContentAudienceKinds.User).Select(r => r.RefId).Where(id => !active.Contains(id)).ToList();
			if (picked.Count > 0)
				result.Skipped = await ExplainSkippedAsync(departmentId, picked);

			return result;
		}

		public async Task<ContentAudienceMemberFacts> GetMemberFactsAsync(int departmentId, string userId)
		{
			var facts = new ContentAudienceMemberFacts { UserId = userId };
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return facts;

			var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId);
			facts.IsActive = DepartmentMemberStateHelper.IsActiveMember(member, departmentId);
			if (!facts.IsActive)
				return facts;

			var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
			if (group != null && group.DepartmentId == departmentId)
				facts.GroupIds.Add(group.DepartmentGroupId);

			foreach (var role in await _personnelRolesService.GetRolesForUserAsync(userId, departmentId) ?? new List<PersonnelRole>())
				if (role != null && role.DepartmentId == departmentId)
					facts.RoleIds.Add(role.PersonnelRoleId);

			var now = DateTime.UtcNow;
			foreach (var certification in await _certificationService.GetCertificationsByUserIdAsync(userId) ?? new List<PersonnelCertification>())
				if (IsCurrentCertification(certification, departmentId, now))
					facts.CertificationTypeIds.Add(certification.DepartmentCertificationTypeId.Value);

			var activeRoles = (await _unitsService.GetAllActiveRolesForUnitsByDepartmentIdAsync(departmentId) ?? new List<UnitActiveRole>())
				.Where(r => string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();
			if (activeRoles.Count > 0)
			{
				var unitIds = new HashSet<int>(activeRoles.Select(r => r.UnitId));
				var typeNames = new HashSet<string>((await _unitsService.GetUnitsForDepartmentAsync(departmentId) ?? new List<Unit>())
					.Where(u => unitIds.Contains(u.UnitId) && !string.IsNullOrWhiteSpace(u.Type)).Select(u => u.Type), StringComparer.OrdinalIgnoreCase);
				foreach (var type in await _unitsService.GetUnitTypesForDepartmentAsync(departmentId) ?? new List<UnitType>())
					if (type != null && typeNames.Contains(type.Type ?? string.Empty))
						facts.UnitTypeIds.Add(type.UnitTypeId);
			}

			var shiftPersons = await _shiftsService.GetShiftPersonsForUserAsync(userId) ?? new List<ShiftPerson>();
			if (shiftPersons.Count > 0)
			{
				var departmentShiftIds = new HashSet<int>((await _shiftsService.GetAllShiftsByDepartmentAsync(departmentId) ?? new List<Shift>()).Select(s => s.ShiftId));
				foreach (var person in shiftPersons)
					if (person != null && departmentShiftIds.Contains(person.ShiftId))
						facts.ShiftIds.Add(person.ShiftId);
			}

			return facts;
		}

		public async Task<bool> IsUserInAudienceAsync(int departmentId, string userId, IEnumerable<IContentAudienceRule> rules)
		{
			var list = rules?.ToList();
			if (list == null || list.Count == 0)
				return false;

			return ContentAudienceMatcher.Matches(await GetMemberFactsAsync(departmentId, userId), list);
		}

		public async Task<List<ContentAudienceSkip>> ExplainSkippedAsync(int departmentId, IEnumerable<string> pickedUserIds)
		{
			var skipped = new List<ContentAudienceSkip>();
			var picked = (pickedUserIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (picked.Count == 0)
				return skipped;

			var members = (await _departmentsService.GetAllMembersForDepartmentIncludingDeletedAsync(departmentId) ?? new List<DepartmentMember>())
				.Where(m => m != null && !string.IsNullOrWhiteSpace(m.UserId))
				.GroupBy(m => m.UserId, StringComparer.OrdinalIgnoreCase)
				// A member who left and came back has an old deleted row too; the live row decides.
				.ToDictionary(g => g.Key, g => g.OrderBy(m => m.IsDeleted ? 1 : 0).First(), StringComparer.OrdinalIgnoreCase);

			foreach (var userId in picked)
			{
				if (!members.TryGetValue(userId, out var member))
					skipped.Add(new ContentAudienceSkip { UserId = userId, Reason = ContentAudienceSkipReasons.NotInDepartment });
				else if (member.IsDeleted)
					skipped.Add(new ContentAudienceSkip { UserId = userId, Reason = ContentAudienceSkipReasons.Deleted });
				else if (member.IsDisabled.GetValueOrDefault())
					skipped.Add(new ContentAudienceSkip { UserId = userId, Reason = ContentAudienceSkipReasons.Disabled });
				else if (member.IsImportDormant)
					skipped.Add(new ContentAudienceSkip { UserId = userId, Reason = ContentAudienceSkipReasons.NotActivated });
				else if (member.IsHidden.GetValueOrDefault())
					skipped.Add(new ContentAudienceSkip { UserId = userId, Reason = ContentAudienceSkipReasons.Hidden });
			}

			return skipped;
		}
	}

	/// <summary>Queues membership changes for worker 83. Never throws.</summary>
	public class ContentAudienceChangeQueue : IContentAudienceChangeQueue
	{
		private readonly IContentAudienceChangesRepository _changesRepository;

		public ContentAudienceChangeQueue(IContentAudienceChangesRepository changesRepository)
		{
			_changesRepository = changesRepository;
		}

		public async Task EnqueueAsync(int departmentId, string userId, ContentAudienceChangeSources source, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0)
				return;

			try
			{
				await _changesRepository.InsertAsync(new ContentAudienceChange
				{
					DepartmentId = departmentId,
					UserId = string.IsNullOrWhiteSpace(userId) ? null : userId,
					Source = (int)source,
					QueuedOn = DateTime.UtcNow
				}, cancellationToken, true);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				// The membership change itself is saved; a missed enqueue is caught up when the member next opens My Training.
				Logging.LogException(ex, $"Could not queue an audience change for department {departmentId}");
			}
		}

		public async Task EnqueueManyAsync(int departmentId, IEnumerable<string> userIds, ContentAudienceChangeSources source, CancellationToken cancellationToken = default)
		{
			foreach (var userId in (userIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
				await EnqueueAsync(departmentId, userId, source, cancellationToken);
		}
	}
}
