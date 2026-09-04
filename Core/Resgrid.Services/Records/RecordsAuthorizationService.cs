using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Per-Record visibility (RMS plan section 5.7.1), following the CanUserViewPersonAsync /
	/// CanUserViewUnitAsync exemplars: read the ViewGroupRecords row, branch on PermissionActions, honor
	/// LockToGroup, fail closed on evaluation error, cache the group set briefly. Always visible regardless
	/// of group: the author, the current owner/reviewer/approver, a named participant, a responding unit's
	/// crew (unit group), department administrators, and a Record shared to the viewer's group.
	/// </summary>
	public class RecordsAuthorizationService : IRecordsAuthorizationService
	{
		private const string VisibleGroupsCacheKey = "RmsVisibleGroups_{0}_{1}";
		private static readonly TimeSpan VisibleGroupsCacheLength = TimeSpan.FromMinutes(2);

		private readonly IPermissionsService _permissionsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IRmsOperationalRecordsRepository _recordsRepository;
		private readonly IRmsRecordGroupScopesRepository _groupScopesRepository;
		private readonly IRmsRecordParticipantsRepository _participantsRepository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IRmsLegacyStatsRepository _legacyStats;
		private readonly IRmsIncidentReportsRepository _incidentReports;

		public RecordsAuthorizationService(IPermissionsService permissionsService, IDepartmentsService departmentsService,
			IDepartmentGroupsService departmentGroupsService, IPersonnelRolesService personnelRolesService, IDepartmentSettingsService departmentSettingsService,
			IRmsOperationalRecordsRepository recordsRepository, IRmsRecordGroupScopesRepository groupScopesRepository,
			IRmsRecordParticipantsRepository participantsRepository, ICacheProvider cacheProvider, IRmsLegacyStatsRepository legacyStats, IRmsIncidentReportsRepository incidentReports)
		{
			_legacyStats = legacyStats;
			_incidentReports = incidentReports;
			_permissionsService = permissionsService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_departmentSettingsService = departmentSettingsService;
			_recordsRepository = recordsRepository;
			_groupScopesRepository = groupScopesRepository;
			_participantsRepository = participantsRepository;
			_cacheProvider = cacheProvider;
		}

		public async Task<bool> IsGroupScopedAsync(int departmentId)
		{
			if (await _departmentSettingsService.GetRecordsGroupVisibilityModeAsync(departmentId) != RecordsGroupVisibilityMode.GroupScoped)
				return false;

			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ViewGroupRecords);
			return permission != null && permission.LockToGroup;
		}

		public async Task<List<int>> GetVisibleGroupIdsAsync(string userId, int departmentId)
		{
			async Task<List<int>> resolve()
			{
				try
				{
					if (!await IsGroupScopedAsync(departmentId))
						return null;

					var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
					if (department == null)
						return new List<int>();

					if (department.IsUserAnAdmin(userId))
						return null;

					var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ViewGroupRecords);
					var group = await _departmentGroupsService.GetGroupForUserAsync(userId, departmentId);
					var isGroupAdmin = group != null && group.IsUserGroupAdmin(userId);
					var roles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId) ?? new List<PersonnelRole>();

					// The row's Action decides who the cross-group rule widens for; everyone else is confined to their own group.
					var seesEverything = permission == null || Resgrid.Model.RecordPermissionEvaluation.IsSatisfied(permission.Action, permission.Data, department.IsUserAnAdmin(userId), isGroupAdmin, roles) && !permission.LockToGroup;
					if (seesEverything)
						return null;

					return group == null ? new List<int>() : new List<int> { group.DepartmentGroupId };
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Record visibility evaluation failed for user {userId} in department {departmentId}; failing closed.");
					return new List<int>();
				}
			}

			if (Config.SystemBehaviorConfig.CacheEnabled)
			{
				var cached = await _cacheProvider.RetrieveAsync<VisibleGroupsCacheEntry>(string.Format(VisibleGroupsCacheKey, departmentId, userId),
					async () => new VisibleGroupsCacheEntry { DepartmentId = departmentId, Unrestricted = (await resolve()) == null, GroupIds = await resolve() ?? new List<int>() },
					VisibleGroupsCacheLength);

				if (cached != null && cached.DepartmentId == departmentId)
					return cached.Unrestricted ? null : cached.GroupIds;
			}

			return await resolve();
		}

		public async Task<bool> CanUserViewRecordAsync(string userId, string recordId, int departmentId)
		{
			try
			{
				// Both aggregates share the id space, the group-scope table and this rule (RMS-2 incident reports have no participants).
				string author, owner, reviewer, approver;
				var isOperational = true;
				var record = await _recordsRepository.GetByIdForDepartmentAsync(departmentId, recordId);
				if (record != null && !record.DeletedOn.HasValue)
				{
					author = record.AuthorUserId; owner = record.OwnerUserId; reviewer = record.ReviewerUserId; approver = record.ApproverUserId;
				}
				else
				{
					var report = await _incidentReports.GetByIdForDepartmentAsync(departmentId, recordId);
					if (report == null || report.DeletedOn.HasValue)
						return false;
					isOperational = false;
					author = report.AuthorUserId; owner = report.OwnerUserId; reviewer = report.ReviewerUserId; approver = null;
				}

				// Always-visible cases resolve before any group evaluation.
				if (string.Equals(author, userId, StringComparison.Ordinal) ||
					string.Equals(owner, userId, StringComparison.Ordinal) ||
					string.Equals(reviewer, userId, StringComparison.Ordinal) ||
					string.Equals(approver, userId, StringComparison.Ordinal))
					return true;

				var visible = await GetVisibleGroupIdsAsync(userId, departmentId);
				if (visible == null)
					return true;

				if (isOperational)
				{
					var participants = await _participantsRepository.GetForRecordAsync(departmentId, recordId, null);
					if (participants != null && participants.Any(p => string.Equals(p.UserId, userId, StringComparison.Ordinal)))
						return true;
				}

				var scope = await _groupScopesRepository.GetForRecordAsync(departmentId, recordId);
				return scope != null && scope.Any(s => visible.Contains(s.DepartmentGroupId));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"CanUserViewRecordAsync failed for record {recordId}; failing closed.");
				return false;
			}
		}

		public async Task<bool> CanSystemPrincipalViewRecordAsync(SystemPrincipalRecordGrant grant, string recordId)
		{
			if (grant == null || string.IsNullOrWhiteSpace(recordId))
				return false;

			try
			{
				// The record must exist, live, in the granted department. Both aggregates share the id space.
				var record = await _recordsRepository.GetByIdForDepartmentAsync(grant.DepartmentId, recordId);
				var exists = record != null && !record.DeletedOn.HasValue;
				if (!exists)
				{
					var report = await _incidentReports.GetByIdForDepartmentAsync(grant.DepartmentId, recordId);
					exists = report != null && !report.DeletedOn.HasValue;
				}

				if (!exists)
					return false;

				if (grant.DepartmentWide)
					return true;

				if (grant.GroupIds.Count == 0)
					return false;

				var scope = await _groupScopesRepository.GetForRecordAsync(grant.DepartmentId, recordId);
				return scope != null && scope.Any(s => grant.GroupIds.Contains(s.DepartmentGroupId));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"CanSystemPrincipalViewRecordAsync failed for record {recordId}; failing closed.");
				return false;
			}
		}

		private class VisibleGroupsCacheEntry
		{
			public int DepartmentId { get; set; }
			public bool Unrestricted { get; set; }
			public List<int> GroupIds { get; set; }
		}

		public async Task<RecordsGroupScopePreview> PreviewGroupScopingAsync(int departmentId)
		{
			var preview = new RecordsGroupScopePreview
			{
				TotalRecords = await _recordsRepository.CountAllAsync(departmentId),
				RecordsWithoutGroupAnchor = await _recordsRepository.CountWithoutGroupScopeAsync(departmentId)
			};

			var legacy = await _legacyStats.GetLegacyStatsAsync(departmentId);
			preview.LegacyLogsWithoutGroup = legacy?.LogsWithoutGroupCount ?? 0;
			preview.LegacyUnitLogs = legacy?.UnitLogCount ?? 0;

			var perGroup = await _groupScopesRepository.CountRecordsByGroupAsync(departmentId) ?? new Dictionary<int, int>();
			var groupedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var group in await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>())
			{
				var members = (await _departmentGroupsService.GetAllMembersForGroupAsync(group.DepartmentGroupId) ?? new List<DepartmentGroupMember>())
					.Where(m => !string.IsNullOrWhiteSpace(m.UserId)).Select(m => m.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
				foreach (var member in members)
					groupedUsers.Add(member);

				preview.Groups.Add(new RecordsGroupScopePreviewGroup
				{
					GroupId = group.DepartmentGroupId,
					GroupName = group.Name,
					MemberCount = members.Count,
					RecordCount = perGroup.TryGetValue(group.DepartmentGroupId, out var count) ? count : 0
				});
			}

			var users = (await _departmentsService.GetAllUsersForDepartmentAsync(departmentId) ?? new List<Resgrid.Model.Identity.IdentityUser>())
				.Where(u => !string.IsNullOrWhiteSpace(u.UserId)).Select(u => u.UserId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			preview.UsersInDepartment = users.Count;
			preview.UsersWithoutGroup = users.Count(u => !groupedUsers.Contains(u));
			preview.DepartmentAdmins = (await _departmentsService.GetAllAdminsForDepartmentAsync(departmentId))?.Count ?? 0;

			return preview;
		}
	}
}
