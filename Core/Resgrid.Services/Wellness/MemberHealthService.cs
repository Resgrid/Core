using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Wellness
{
	/// <summary>
	/// Member wellness WL1 (protocols-training-knowledge-plan §13b). See <see cref="IMemberHealthService"/> for the access rules. Protected
	/// values ride the generic Records seam with ADP catalog 38 (<see cref="MemberHealthProtectedFields"/>): writes encrypt at rest when the
	/// department enforces protection, reads reveal for the caller's grant and show REDACTED otherwise. Audit entries carry ids, stages,
	/// statuses and dates, never a protected value. Notices name no health detail. Nothing here is indexed for search or sent to an AI
	/// feature (§16.3).
	/// </summary>
	public partial class MemberHealthService : IMemberHealthService
	{
		public const int WorkerBatch = 200;

		private readonly IRmsExposureIncidentsRepository _incidents;
		private readonly IRmsMemberInjuriesRepository _injuries;
		private readonly IRmsInjuryDutyPeriodsRepository _periods;
		private readonly IRmsMedicalClearancesRepository _clearances;
		private readonly IRmsResponderExposuresRepository _exposures;
		private readonly IRmsRecordWorkAssignmentsRepository _assignments;
		private readonly IRmsRecordLegalHoldsRepository _legalHolds;
		private readonly IMemberWellnessService _wellness;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _settingsService;
		private readonly IPersonnelRolesService _rolesService;
		private readonly IDepartmentGroupsService _groupsService;
		private readonly Lazy<ICertificationService> _certificationService;
		private readonly ICallsService _callsService;
		private readonly Lazy<ICommunicationService> _communicationService;
		private readonly Lazy<IProtectedWriteService> _protectedWrite;
		private readonly Lazy<IProtectedReadService> _protectedRead;
		private readonly IProtectedGrantContext _grant;
		private readonly IEventAggregator _events;

		public MemberHealthService(IRmsExposureIncidentsRepository incidents, IRmsMemberInjuriesRepository injuries, IRmsInjuryDutyPeriodsRepository periods,
			IRmsMedicalClearancesRepository clearances, IRmsResponderExposuresRepository exposures, IRmsRecordWorkAssignmentsRepository assignments,
			IRmsRecordLegalHoldsRepository legalHolds, IMemberWellnessService wellness, IDepartmentsService departmentsService, IDepartmentSettingsService settingsService,
			IPersonnelRolesService rolesService, IDepartmentGroupsService groupsService, Lazy<ICertificationService> certificationService, ICallsService callsService,
			Lazy<ICommunicationService> communicationService, Lazy<IProtectedWriteService> protectedWrite, Lazy<IProtectedReadService> protectedRead,
			IProtectedGrantContext grant, IEventAggregator events)
		{
			_incidents = incidents;
			_injuries = injuries;
			_periods = periods;
			_clearances = clearances;
			_exposures = exposures;
			_assignments = assignments;
			_legalHolds = legalHolds;
			_wellness = wellness;
			_departmentsService = departmentsService;
			_settingsService = settingsService;
			_rolesService = rolesService;
			_groupsService = groupsService;
			_certificationService = certificationService;
			_callsService = callsService;
			_communicationService = communicationService;
			_protectedWrite = protectedWrite;
			_protectedRead = protectedRead;
			_grant = grant;
			_events = events;
		}

		#region Access

		public async Task<MemberHealthAccess> GetAccessAsync(int departmentId, string userId)
		{
			var access = new MemberHealthAccess();
			if (string.IsNullOrWhiteSpace(userId))
				return access;
			try
			{
				var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId, true);
				if (!DepartmentMemberStateHelper.IsCurrentMember(member, departmentId))
					return access;
				access.IsMember = true;

				var settings = await _wellness.GetSettingsAsync(departmentId) ?? new WellnessSettings();
				var designatedRoleId = DesignatedRoleId(settings);
				if (settings.WellnessOfficerRoleId > 0 || designatedRoleId > 0)
				{
					var roles = await _rolesService.GetRolesForUserAsync(userId, departmentId) ?? new List<PersonnelRole>();
					access.IsWellnessOfficer = settings.WellnessOfficerRoleId > 0 && roles.Any(r => r != null && r.PersonnelRoleId == settings.WellnessOfficerRoleId);
					access.IsDesignatedOfficer = designatedRoleId > 0 && roles.Any(r => r != null && r.PersonnelRoleId == designatedRoleId);
				}

				access.IsAdmin = await _wellness.IsDepartmentAdminAsync(departmentId, userId);
				access.IsSupervisor = access.IsAdmin || (await _groupsService.GetGroupMemberForUserAsync(userId, departmentId))?.IsAdmin == true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Member health access check failed for department {departmentId}.");
				return new MemberHealthAccess();
			}
			return access;
		}

		/// <summary>The designated officer role, or the wellness officer role when the department names none.</summary>
		internal static int DesignatedRoleId(WellnessSettings settings) =>
			settings == null ? 0 : settings.DesignatedOfficerRoleId > 0 ? settings.DesignatedOfficerRoleId : settings.WellnessOfficerRoleId;

		public async Task<bool> IsDesignatedOfficerAsync(int departmentId, string userId) => (await GetAccessAsync(departmentId, userId)).IsDesignatedOfficer;

		public async Task<bool> IsHealthOfficerAsync(int departmentId, string userId) => (await GetAccessAsync(departmentId, userId)).IsHealthOfficer;

		private async Task<bool> IsCurrentMemberAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return false;
			var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId, true);
			return DepartmentMemberStateHelper.IsCurrentMember(member, departmentId);
		}

		private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

		/// <summary>The member themselves (still current) or an officer of the right kind.</summary>
		private async Task<bool> CanReadAsync(int departmentId, string viewerUserId, string memberUserId, bool designatedOnly)
		{
			if (string.IsNullOrWhiteSpace(viewerUserId) || string.IsNullOrWhiteSpace(memberUserId))
				return false;
			if (Same(viewerUserId, memberUserId))
				return await IsCurrentMemberAsync(departmentId, viewerUserId);
			var access = await GetAccessAsync(departmentId, viewerUserId);
			return designatedOnly ? access.IsDesignatedOfficer : access.IsHealthOfficer;
		}

		private async Task RequireDesignatedOfficerAsync(int departmentId, string userId)
		{
			if (!await IsDesignatedOfficerAsync(departmentId, userId))
				throw new UnauthorizedAccessException("This needs the department's designated officer role.");
		}

		private async Task RequireHealthOfficerAsync(int departmentId, string userId)
		{
			if (!await IsHealthOfficerAsync(departmentId, userId))
				throw new UnauthorizedAccessException("This needs the department's designated or wellness officer role.");
		}

		/// <summary>The current members of a role (case-insensitive ids), active ones only when <paramref name="activeOnly"/>.</summary>
		private async Task<List<string>> RoleMembersAsync(int departmentId, int roleId)
		{
			if (roleId <= 0)
				return new List<string>();
			var active = await _departmentsService.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			return (await _rolesService.GetAllMembersOfRoleAsync(roleId) ?? new List<PersonnelRoleUser>())
				.Where(m => m != null && !string.IsNullOrWhiteSpace(m.UserId) && active.Contains(m.UserId))
				.Select(m => m.UserId)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		private async Task<List<string>> DesignatedOfficersAsync(int departmentId)
		{
			var settings = await _wellness.GetSettingsAsync(departmentId) ?? new WellnessSettings();
			return await RoleMembersAsync(departmentId, DesignatedRoleId(settings));
		}

		private async Task<List<string>> HealthOfficersAsync(int departmentId)
		{
			var settings = await _wellness.GetSettingsAsync(departmentId) ?? new WellnessSettings();
			var ids = await RoleMembersAsync(departmentId, DesignatedRoleId(settings));
			if (settings.WellnessOfficerRoleId > 0 && settings.WellnessOfficerRoleId != DesignatedRoleId(settings))
				ids.AddRange(await RoleMembersAsync(departmentId, settings.WellnessOfficerRoleId));
			return ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		#endregion Access

		#region Shared helpers

		private async Task<Department> DepartmentAsync(int departmentId) => await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

		/// <summary>A UTC instant as the department-local calendar date.</summary>
		public static DateTime LocalDate(DateTime utc, Department department) =>
			department == null ? utc.Date : DateTime.SpecifyKind(utc, DateTimeKind.Utc).TimeConverter(department).Date;

		private async Task ValidateCallAsync(int departmentId, int? callId)
		{
			if (!callId.HasValue)
				return;
			var call = await _callsService.GetCallByIdAsync(callId.Value);
			if (call == null || call.DepartmentId != departmentId)
				throw new MemberWellnessException("call_not_found");
		}

		private static void ValidateDate(DateTime value, string code)
		{
			if (value == default || value > DateTime.UtcNow.AddDays(1) || value < new DateTime(1950, 1, 1))
				throw new MemberWellnessException(code);
		}

		private static string Trim(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var trimmed = value.Trim();
			return trimmed.Length > max ? trimmed.Substring(0, max) : trimmed;
		}

		/// <summary>
		/// A protected value the form could not show (withheld without a grant) comes back as the redaction placeholder: the stored value
		/// stays as it is rather than being overwritten. Anything else is trimmed and replaces it.
		/// </summary>
		internal static string Keep(string input, string stored, int max) => input == ProtectedDataEnvelope.RedactionValue ? stored : Trim(input, max);

		private static T Clone<T>(T row) where T : class =>
			(T)row.GetType().GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(row, null);

		private static string Code(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after)
		{
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Before = before,
				After = after,
				Successful = true
			});
		}

		/// <summary>Direct notices to active members only; a failed channel never stops the others or undoes the write.</summary>
		private async Task<int> NotifyAsync(int departmentId, IEnumerable<string> userIds, string title, string message, EventTypes type, string id, CancellationToken cancellationToken)
		{
			var wanted = (userIds ?? Enumerable.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (wanted.Count == 0 || !ConfigHelper.CanTransmit(departmentId))
				return 0;
			var sent = 0;
			try
			{
				var department = await DepartmentAsync(departmentId);
				if (department == null)
					return 0;
				var active = await _departmentsService.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				var number = await _settingsService.GetTextToCallNumberForDepartmentAsync(departmentId);
				var eventCode = (type == EventTypes.ExposureIncidentNotice ? "EI:" : "DS:") + id;
				foreach (var userId in wanted.Where(active.Contains))
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						if (await _communicationService.Value.SendNotificationAsync(userId, departmentId, message, number, department, title, null, false, eventCode))
							sent++;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"Member health notice to {userId} in department {departmentId} failed.");
					}
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Member health notices failed for department {departmentId}.");
			}
			return sent;
		}

		private static string Link(string path) => (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + path;

		private async Task<string> NameOfAsync(int departmentId, string userId)
		{
			var names = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>();
			var name = names.FirstOrDefault(n => Same(n.UserId, userId))?.Name?.Trim();
			return string.IsNullOrWhiteSpace(name) ? "A member" : name;
		}

		#endregion Shared helpers

		#region Protection

		private async Task ProtectAsync<T>(int departmentId, T row, T existing, string rowKey,
			IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors, Action markProtected, CancellationToken cancellationToken) where T : class
		{
			var result = await _protectedWrite.Value.PrepareRecordsEntityWriteAsync(departmentId, row, existing, rowKey, accessors, markProtected, null, null, true, cancellationToken);
			if (result != null && !result.Success)
				throw new MemberWellnessException("protected_write_refused");
		}

		private Task ProtectAsync(int departmentId, RmsExposureIncident row, RmsExposureIncident existing, CancellationToken cancellationToken) =>
			ProtectAsync(departmentId, row, existing, row.RmsExposureIncidentId, MemberHealthProtectedFields.ExposureIncidents,
				() => { row.IsProtected = true; row.ProtectedCatalogVersion = Math.Max(row.ProtectedCatalogVersion, MemberHealthProtectedFields.CatalogVersion); }, cancellationToken);

		private Task ProtectAsync(int departmentId, RmsMemberInjury row, RmsMemberInjury existing, CancellationToken cancellationToken) =>
			ProtectAsync(departmentId, row, existing, row.RmsMemberInjuryId, MemberHealthProtectedFields.Injuries,
				() => { row.IsProtected = true; row.ProtectedCatalogVersion = Math.Max(row.ProtectedCatalogVersion, MemberHealthProtectedFields.CatalogVersion); }, cancellationToken);

		private Task ProtectAsync(int departmentId, RmsMedicalClearance row, RmsMedicalClearance existing, CancellationToken cancellationToken) =>
			ProtectAsync(departmentId, row, existing, row.RmsMedicalClearanceId, MemberHealthProtectedFields.Clearances,
				() => { row.IsProtected = true; row.ProtectedCatalogVersion = Math.Max(row.ProtectedCatalogVersion, MemberHealthProtectedFields.CatalogVersion); }, cancellationToken);

		/// <summary>Reveals for the caller's grant; true when something stayed withheld. Never throws (a failure withholds).</summary>
		private async Task<bool> RevealAsync<T>(int departmentId, List<T> rows, Func<T, string> key, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors) where T : class
		{
			if (rows == null || rows.Count == 0)
				return false;
			try
			{
				var result = await _protectedRead.Value.ResolveRecordsEntitiesForReadAsync(departmentId, rows.Select(r => (r, key(r))).ToList(), accessors, _grant?.GrantToken, _grant?.UserId);
				return result != null && result.RedactedFields != null && result.RedactedFields.Count > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Protected member health records could not be resolved for read.");
				foreach (var row in rows)
					foreach (var accessor in accessors.Values)
						if (ProtectedDataEnvelope.HasEnvelopePrefix(accessor.Get(row)))
							accessor.Set(row, ProtectedDataEnvelope.RedactionValue);
				return true;
			}
		}

		private Task<bool> RevealAsync(int departmentId, List<RmsExposureIncident> rows) => RevealAsync(departmentId, rows, r => r.RmsExposureIncidentId, MemberHealthProtectedFields.ExposureIncidents);

		private Task<bool> RevealAsync(int departmentId, List<RmsMemberInjury> rows) => RevealAsync(departmentId, rows, r => r.RmsMemberInjuryId, MemberHealthProtectedFields.Injuries);

		private Task<bool> RevealAsync(int departmentId, List<RmsMedicalClearance> rows) => RevealAsync(departmentId, rows, r => r.RmsMedicalClearanceId, MemberHealthProtectedFields.Clearances);

		/// <summary>Blanks every protected value (lists that carry operational columns only).</summary>
		private static void Blank<T>(IEnumerable<T> rows, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors)
		{
			foreach (var row in rows ?? Enumerable.Empty<T>())
				foreach (var accessor in accessors.Values)
					accessor.Set(row, null);
		}

		#endregion Protection
	}
}
