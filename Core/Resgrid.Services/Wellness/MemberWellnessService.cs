using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
	/// Member wellness WL0 (protocols-training-knowledge-plan §13b). See <see cref="IMemberWellnessService"/> for the access rules.
	/// Protected values ride the generic Records seam with ADP catalog 37 (<see cref="WellnessProtectedFields"/>): writes encrypt at
	/// rest as a workload caller when the department enforces protection, reads reveal for the caller's grant and show REDACTED
	/// otherwise. Audit entries carry ids, categories and statuses, never a protected value. Nothing here is indexed for search or
	/// sent to any AI feature (§16.3).
	/// <para>
	/// Workforce Pro (owner 2026-11-26): every read and write first asks the Workforce Pro gate (<see cref="WorkforceProAccess"/>). With the
	/// Workforce.Pro flag off everything refuses (the routes 404); offered but not licensed, records stay readable by those already permitted
	/// (a member always reads and exports their own) and nothing is created, edited or deleted; licensed, a record about a member is created
	/// or edited only while that member holds a seat. The unattended GDPR archive and the retention erasure are legal duties and never gated.
	/// </para>
	/// </summary>
	public partial class MemberWellnessService : IMemberWellnessService
	{
		public const int WorkerBatch = 200;

		private readonly IRmsResponderExposuresRepository _exposures;
		private readonly IRmsCallExposureFlagsRepository _callFlags;
		private readonly IRmsResponderScreeningFlagsRepository _screeningFlags;
		private readonly IRmsRecordWorkAssignmentsRepository _assignments;
		private readonly IRmsRecordLegalHoldsRepository _legalHolds;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _settingsService;
		private readonly IPersonnelRolesService _rolesService;
		private readonly Lazy<ICertificationService> _certificationService;
		private readonly ICallsService _callsService;
		private readonly Lazy<IAuthorizationService> _authorizationService;
		private readonly Lazy<IActionLogsService> _actionLogsService;
		private readonly Lazy<IUnitsService> _unitsService;
		private readonly Lazy<ICheckInTimerService> _checkInService;
		private readonly Lazy<ICommunicationService> _communicationService;
		private readonly Lazy<IProtectedWriteService> _protectedWrite;
		private readonly Lazy<IProtectedReadService> _protectedRead;
		private readonly IProtectedGrantContext _grant;
		private readonly IEventAggregator _events;
		private readonly IUserProfileService _userProfiles;
		private readonly WellnessRoleResolver _roleResolver;
		private readonly IWorkforceProAccessService _workforcePro;

		public MemberWellnessService(IRmsResponderExposuresRepository exposures, IRmsCallExposureFlagsRepository callFlags,
			IRmsResponderScreeningFlagsRepository screeningFlags, IRmsRecordWorkAssignmentsRepository assignments, IRmsRecordLegalHoldsRepository legalHolds,
			IDepartmentsService departmentsService, IDepartmentSettingsService settingsService, IPersonnelRolesService rolesService,
			Lazy<ICertificationService> certificationService, ICallsService callsService, Lazy<IAuthorizationService> authorizationService,
			Lazy<IActionLogsService> actionLogsService, Lazy<IUnitsService> unitsService, Lazy<ICheckInTimerService> checkInService,
			Lazy<ICommunicationService> communicationService, Lazy<IProtectedWriteService> protectedWrite, Lazy<IProtectedReadService> protectedRead,
			IProtectedGrantContext grant, IEventAggregator events, IUserProfileService userProfiles, IWorkforceProAccessService workforcePro)
		{
			_workforcePro = workforcePro;
			_userProfiles = userProfiles;
			_exposures = exposures;
			_callFlags = callFlags;
			_screeningFlags = screeningFlags;
			_assignments = assignments;
			_legalHolds = legalHolds;
			_departmentsService = departmentsService;
			_settingsService = settingsService;
			_rolesService = rolesService;
			_certificationService = certificationService;
			_callsService = callsService;
			_authorizationService = authorizationService;
			_actionLogsService = actionLogsService;
			_unitsService = unitsService;
			_checkInService = checkInService;
			_communicationService = communicationService;
			_protectedWrite = protectedWrite;
			_protectedRead = protectedRead;
			_grant = grant;
			_events = events;
			_roleResolver = new WellnessRoleResolver(departmentsService, rolesService, certificationService);
		}

		#region Access

		public Task<WellnessSettings> GetSettingsAsync(int departmentId) => _settingsService.GetWellnessSettingsAsync(departmentId);

		public async Task<WellnessSettings> SaveSettingsAsync(int departmentId, string adminUserId, WellnessSettings settings, CancellationToken cancellationToken = default)
		{
			await RequireReadableAsync(departmentId);
			if (!await IsDepartmentAdminAsync(departmentId, adminUserId))
				throw new UnauthorizedAccessException("Only a department administrator may change the wellness settings.");
			// Read-only while Workforce Pro is not licensed: the settings stay as they are.
			await RequireWritableAsync(departmentId);

			var stored = (settings ?? new WellnessSettings()).Normalized();
			if (stored.WellnessOfficerRoleId > 0 || stored.DesignatedOfficerRoleId > 0 || stored.PeerTeamRoleId > 0)
			{
				var roles = await _rolesService.GetRolesForDepartmentAsync(departmentId) ?? new List<PersonnelRole>();
				// WL1: the designated officer role is checked like the wellness officer role; so is the peer support role (2026-11-26).
				foreach (var roleId in new[] { stored.WellnessOfficerRoleId, stored.DesignatedOfficerRoleId, stored.PeerTeamRoleId }.Where(id => id > 0))
					if (!roles.Any(r => r.PersonnelRoleId == roleId))
						throw new MemberWellnessException("role_not_found");
			}
			// WL1 adds the BBP training and medical clearance certification types; every named type must be the department's own.
			foreach (var typeId in new[] { stored.PeerTeamCertificationTypeId, stored.BbpTrainingCertificationTypeId, stored.MedicalClearanceCertificationTypeId }.Where(id => id > 0))
			{
				var type = await _certificationService.Value.GetCertificationTypeByIdAsync(typeId);
				if (type == null || type.DepartmentId != departmentId)
					throw new MemberWellnessException("certification_type_not_found");
			}
			// The officers' own certification types (2026-11-26) are person-scoped types of the department that still exist.
			foreach (var typeId in new[] { stored.WellnessOfficerCertificationTypeId, stored.DesignatedOfficerCertificationTypeId }.Where(id => id > 0))
				if (await _roleResolver.TypeAsync(departmentId, typeId) == null)
					throw new MemberWellnessException("certification_type_not_found");

			var before = await GetSettingsAsync(departmentId);
			// Turning the switch on must not leave an officer role empty: logs and exposure reports would have nobody to go to.
			if (stored.RequireCurrentCertification && before?.RequireCurrentCertification != true)
			{
				foreach (var kind in new[] { WellnessRoleKinds.WellnessOfficer, WellnessRoleKinds.DesignatedOfficer })
				{
					if (!WellnessRoleRules.RequiresCertification(stored, kind) || WellnessRoleRules.RoleIdFor(stored, kind) <= 0)
						continue;
					var roster = await _roleResolver.RosterAsync(departmentId, stored, kind);
					if (!roster.CertificationTypeMissing && roster.ActingCount == 0)
						throw new MemberWellnessException("certification_switch_leaves_no_officer");
				}
			}
			await _settingsService.SetWellnessSettingsAsync(departmentId, stored, cancellationToken);
			Audit(departmentId, adminUserId, AuditLogTypes.WellnessSettingsChanged, JsonConvert.SerializeObject(before), JsonConvert.SerializeObject(stored));
			return stored;
		}

		public async Task<bool> IsWellnessOfficerAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return false;
			try
			{
				var settings = await GetSettingsAsync(departmentId);
				if (settings.WellnessOfficerRoleId <= 0)
					return false;
				if (!await IsCurrentMemberAsync(departmentId, userId))
					return false;
				var roles = await _rolesService.GetRolesForUserAsync(userId, departmentId) ?? new List<PersonnelRole>();
				if (!roles.Any(r => r != null && r.PersonnelRoleId == settings.WellnessOfficerRoleId))
					return false;
				// 2026-11-26: with the certification switch on, the role also needs a current certification of its linked type.
				return await _roleResolver.PassesCertificationAsync(departmentId, settings, WellnessRoleKinds.WellnessOfficer, userId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Wellness officer check failed for department {departmentId}.");
				return false;
			}
		}

		public async Task<bool> IsDepartmentAdminAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return false;
			var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId, true);
			if (!DepartmentMemberStateHelper.IsCurrentMember(member, departmentId))
				return false;
			if (member.IsAdmin.GetValueOrDefault())
				return true;
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			return department != null && string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase);
		}

		public async Task<bool> CanFlagCallAsync(int departmentId, string userId, int callId)
		{
			if (!await IsCurrentMemberAsync(departmentId, userId))
				return false;
			if (await IsWellnessOfficerAsync(departmentId, userId) || await IsDepartmentAdminAsync(departmentId, userId))
				return true;
			try { return await _authorizationService.Value.CanUserEditCallAsync(userId, callId); }
			catch (Exception ex) { Logging.LogException(ex); return false; }
		}

		public Task<List<string>> GetPeerTeamUserIdsAsync(int departmentId) => GetActingMemberIdsAsync(departmentId, WellnessRoleKinds.PeerSupportTeam);

		public async Task<List<string>> GetActingMemberIdsAsync(int departmentId, WellnessRoleKinds kind)
		{
			var settings = await GetSettingsAsync(departmentId) ?? new WellnessSettings();
			return await _roleResolver.ActingMembersAsync(departmentId, settings, kind);
		}

		public async Task<List<WellnessRoleRoster>> GetRosterAsync(int departmentId, string viewerUserId)
		{
			await RequireReadableAsync(departmentId);
			if (!await IsDepartmentAdminAsync(departmentId, viewerUserId) && !await IsWellnessOfficerAsync(departmentId, viewerUserId))
				throw new UnauthorizedAccessException("The wellness roster is for department administrators and wellness officers.");
			return await _roleResolver.RostersAsync(departmentId, await GetSettingsAsync(departmentId) ?? new WellnessSettings());
		}

		private async Task<bool> IsCurrentMemberAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return false;
			var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId, true);
			return DepartmentMemberStateHelper.IsCurrentMember(member, departmentId);
		}

		private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

		/// <summary>The Workforce Pro gate for the department (one evaluation per request, remembered by the access service).</summary>
		private Task<WorkforceProAccess> WorkforceProAsync(int departmentId) => _workforcePro.GetAccessAsync(departmentId);

		/// <summary>Refuses unless Workforce Pro is offered to the department (flag on).</summary>
		private async Task RequireReadableAsync(int departmentId) => (await WorkforceProAsync(departmentId)).EnsureReadable();

		/// <summary>Refuses unless Workforce Pro is licensed: department-level writes (settings, call flags).</summary>
		private async Task RequireWritableAsync(int departmentId) => (await WorkforceProAsync(departmentId)).EnsureWritable();

		/// <summary>Refuses unless Workforce Pro is licensed and the record's member holds a seat.</summary>
		private async Task RequireWritableForAsync(int departmentId, string subjectUserId) => (await WorkforceProAsync(departmentId)).EnsureWritableFor(subjectUserId);

		/// <summary>The member themselves (still a current member) or a wellness officer.</summary>
		private async Task<bool> CanReadMemberAsync(int departmentId, string viewerUserId, string memberUserId)
		{
			if (string.IsNullOrWhiteSpace(viewerUserId) || string.IsNullOrWhiteSpace(memberUserId))
				return false;
			if (Same(viewerUserId, memberUserId))
				return await IsCurrentMemberAsync(departmentId, viewerUserId);
			return await IsWellnessOfficerAsync(departmentId, viewerUserId);
		}

		private async Task RequireOfficerAsync(int departmentId, string userId)
		{
			if (!await IsWellnessOfficerAsync(departmentId, userId))
				throw new UnauthorizedAccessException("This needs the department's wellness officer role.");
		}

		#endregion Access

		#region Entries

		public async Task<MemberWellnessOverview> GetOverviewAsync(int departmentId, string viewerUserId, string memberUserId)
		{
			await RequireReadableAsync(departmentId);
			if (!await CanReadMemberAsync(departmentId, viewerUserId, memberUserId))
				throw new UnauthorizedAccessException("Exposure entries are restricted to the member and the wellness officers.");

			var rows = (await _exposures.GetForUserAsync(departmentId, memberUserId) ?? Enumerable.Empty<RmsResponderExposure>()).ToList();
			var overview = new MemberWellnessOverview { UserId = memberUserId };
			overview.ProtectedWithheld = await RevealAsync(departmentId, rows);
			overview.Entries = rows.Where(r => r.Status != (int)ResponderExposureStatuses.Suggested && r.Status != (int)ResponderExposureStatuses.Dismissed).ToList();
			overview.Suggestions = rows.Where(r => r.Status == (int)ResponderExposureStatuses.Suggested).ToList();
			overview.ConfirmedByCategory = rows.Where(r => r.Counts).GroupBy(r => r.Category).ToDictionary(g => g.Key, g => g.Count());
			overview.CriticalIncidentCount = overview.ConfirmedByCategory.TryGetValue((int)ResponderExposureCategories.CriticalIncident, out var critical) ? critical : 0;

			var flag = await _screeningFlags.GetActiveForUserAsync(departmentId, memberUserId);
			if (flag != null)
			{
				overview.ProtectedWithheld |= await RevealAsync(departmentId, new List<RmsResponderScreeningFlag> { flag });
				overview.ScreeningFlag = flag;
				overview.FollowUps = await FollowUpsForFlagAsync(departmentId, flag.RmsResponderScreeningFlagId);
			}

			return overview;
		}

		public async Task<RmsResponderExposure> GetEntryAsync(int departmentId, string viewerUserId, string exposureId)
		{
			await RequireReadableAsync(departmentId);
			if (string.IsNullOrWhiteSpace(exposureId))
				return null;
			var row = await _exposures.GetByIdForDepartmentAsync(departmentId, exposureId);
			if (row == null || !await CanReadMemberAsync(departmentId, viewerUserId, row.UserId))
				return null;
			await RevealAsync(departmentId, new List<RmsResponderExposure> { row });
			return row;
		}

		public async Task<RmsResponderExposure> RecordAsync(int departmentId, string actorUserId, ResponderExposureInput input, CancellationToken cancellationToken = default)
		{
			if (input == null)
				throw new ArgumentNullException(nameof(input));
			var memberUserId = string.IsNullOrWhiteSpace(input.UserId) ? actorUserId : input.UserId.Trim();
			await RequireReadableAsync(departmentId);
			await RequireWriterAsync(departmentId, actorUserId, memberUserId);
			if (!await IsCurrentMemberAsync(departmentId, memberUserId))
				throw new MemberWellnessException("member_not_found");
			await RequireWritableForAsync(departmentId, memberUserId);
			await ValidateAsync(departmentId, input);

			var now = DateTime.UtcNow;
			var row = new RmsResponderExposure
			{
				RmsResponderExposureId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				UserId = memberUserId,
				Status = (int)ResponderExposureStatuses.Confirmed,
				Source = (int)ResponderExposureSources.Manual,
				ConfirmedOn = now,
				ConfirmedByUserId = actorUserId,
				CreatedOn = now,
				CreatedByUserId = actorUserId,
				ModifiedOn = now,
				ModifiedByUserId = actorUserId,
				RowVersion = 1
			};
			Apply(row, input);
			// Only the member chooses anniversary reminders for themselves.
			if (Same(actorUserId, memberUserId))
				await ApplyRemindersAsync(departmentId, row, input.RemindMember, input.RemindPeerTeam, now);

			await ProtectAsync(departmentId, row, null, cancellationToken);
			await _exposures.InsertAsync(row, cancellationToken, true);
			Audit(departmentId, actorUserId, AuditLogTypes.ResponderExposureRecorded, null, Snapshot(row));
			return row;
		}

		public async Task<RmsResponderExposure> UpdateAsync(int departmentId, string actorUserId, string exposureId, ResponderExposureInput input, long? expectedRowVersion, CancellationToken cancellationToken = default)
		{
			if (input == null)
				throw new ArgumentNullException(nameof(input));
			var row = await LoadForWriteAsync(departmentId, actorUserId, exposureId);
			if (row.Status == (int)ResponderExposureStatuses.Voided || row.Status == (int)ResponderExposureStatuses.Dismissed)
				throw new MemberWellnessException("entry_closed");
			if (expectedRowVersion.HasValue && expectedRowVersion.Value != row.RowVersion)
				throw new MemberWellnessException("entry_changed");
			// The member and the date the entry is about stay as recorded; a suggestion keeps its call.
			input.UserId = row.UserId;
			if (row.Source == (int)ResponderExposureSources.PostCallSuggestion)
				input.CallId = row.CallId;
			await ValidateAsync(departmentId, input);

			var existing = Clone(row);
			var before = Snapshot(row);
			Apply(row, input);
			var now = DateTime.UtcNow;
			if (Same(actorUserId, row.UserId) && row.Counts)
				await ApplyRemindersAsync(departmentId, row, input.RemindMember, input.RemindPeerTeam, now);
			else if (!MemberWellnessRules.OffersAnniversaryReminders(row.Category))
				ClearReminders(row);

			row.ModifiedOn = now;
			row.ModifiedByUserId = actorUserId;
			row.RowVersion++;
			await ProtectAsync(departmentId, row, existing, cancellationToken);
			await _exposures.UpdateAsync(row, cancellationToken, true);
			Audit(departmentId, actorUserId, AuditLogTypes.ResponderExposureUpdated, before, Snapshot(row));
			return await GetEntryAsync(departmentId, actorUserId, row.RmsResponderExposureId) ?? row;
		}

		public async Task<RmsResponderExposure> ConfirmAsync(int departmentId, string actorUserId, string exposureId, ResponderExposureInput details, CancellationToken cancellationToken = default)
		{
			var row = await LoadForWriteAsync(departmentId, actorUserId, exposureId);
			if (row.Status != (int)ResponderExposureStatuses.Suggested)
				throw new MemberWellnessException("not_a_suggestion");

			var existing = Clone(row);
			var before = Snapshot(row);
			var now = DateTime.UtcNow;
			if (details != null)
			{
				details.UserId = row.UserId;
				details.CallId = row.CallId;
				await ValidateAsync(departmentId, details);
				Apply(row, details);
			}

			row.Status = (int)ResponderExposureStatuses.Confirmed;
			row.ConfirmedOn = now;
			row.ConfirmedByUserId = actorUserId;
			if (details != null && Same(actorUserId, row.UserId))
				await ApplyRemindersAsync(departmentId, row, details.RemindMember, details.RemindPeerTeam, now);
			row.ModifiedOn = now;
			row.ModifiedByUserId = actorUserId;
			row.RowVersion++;
			await ProtectAsync(departmentId, row, existing, cancellationToken);
			await _exposures.UpdateAsync(row, cancellationToken, true);
			Audit(departmentId, actorUserId, AuditLogTypes.ResponderExposureConfirmed, before, Snapshot(row));
			return row;
		}

		public async Task<RmsResponderExposure> DismissAsync(int departmentId, string actorUserId, string exposureId, CancellationToken cancellationToken = default)
		{
			var row = await LoadForWriteAsync(departmentId, actorUserId, exposureId);
			if (row.Status != (int)ResponderExposureStatuses.Suggested)
				throw new MemberWellnessException("not_a_suggestion");
			var before = Snapshot(row);
			var now = DateTime.UtcNow;
			row.Status = (int)ResponderExposureStatuses.Dismissed;
			row.DismissedOn = now;
			row.DismissedByUserId = actorUserId;
			ClearReminders(row);
			row.ModifiedOn = now;
			row.ModifiedByUserId = actorUserId;
			row.RowVersion++;
			await _exposures.UpdateAsync(row, cancellationToken, true);
			Audit(departmentId, actorUserId, AuditLogTypes.ResponderExposureDismissed, before, Snapshot(row));
			return row;
		}

		public async Task<RmsResponderExposure> VoidAsync(int departmentId, string actorUserId, string exposureId, CancellationToken cancellationToken = default)
		{
			var row = await LoadForWriteAsync(departmentId, actorUserId, exposureId);
			if (row.Status != (int)ResponderExposureStatuses.Confirmed)
				throw new MemberWellnessException("not_confirmed");
			var before = Snapshot(row);
			var now = DateTime.UtcNow;
			row.Status = (int)ResponderExposureStatuses.Voided;
			row.VoidedOn = now;
			row.VoidedByUserId = actorUserId;
			ClearReminders(row);
			row.ModifiedOn = now;
			row.ModifiedByUserId = actorUserId;
			row.RowVersion++;
			await _exposures.UpdateAsync(row, cancellationToken, true);
			Audit(departmentId, actorUserId, AuditLogTypes.ResponderExposureVoided, before, Snapshot(row));
			return row;
		}

		public async Task<RmsResponderExposure> SetRemindersAsync(int departmentId, string memberUserId, string exposureId, bool remindMember, bool remindPeerTeam, CancellationToken cancellationToken = default)
		{
			await RequireReadableAsync(departmentId);
			var row = string.IsNullOrWhiteSpace(exposureId) ? null : await _exposures.GetByIdForDepartmentAsync(departmentId, exposureId);
			if (row == null || !Same(row.UserId, memberUserId) || !await IsCurrentMemberAsync(departmentId, memberUserId))
				throw new UnauthorizedAccessException("Only the member chooses anniversary reminders for their own entries.");
			await RequireWritableForAsync(departmentId, row.UserId);
			if (!row.Counts)
				throw new MemberWellnessException("not_confirmed");
			if ((remindMember || remindPeerTeam) && !MemberWellnessRules.OffersAnniversaryReminders(row.Category))
				throw new MemberWellnessException("reminders_critical_incident_only");

			var before = Snapshot(row);
			var now = DateTime.UtcNow;
			await ApplyRemindersAsync(departmentId, row, remindMember, remindPeerTeam, now);
			row.ModifiedOn = now;
			row.ModifiedByUserId = memberUserId;
			row.RowVersion++;
			// The reminder columns are operational: no protected column changes, so the row is written as it is stored.
			await _exposures.UpdateAsync(row, cancellationToken, true);
			Audit(departmentId, memberUserId, AuditLogTypes.ResponderExposureUpdated, before, Snapshot(row));
			return row;
		}

		public async Task<List<RmsResponderExposure>> GetOwnEntriesForExportAsync(int departmentId, string memberUserId)
		{
			// Read-only and lapsed departments keep the member's own export (their access right); only the flag hides it.
			await RequireReadableAsync(departmentId);
			if (!await IsCurrentMemberAsync(departmentId, memberUserId))
				throw new UnauthorizedAccessException("Only a current member exports their own entries.");
			var rows = (await _exposures.GetForUserAsync(departmentId, memberUserId) ?? Enumerable.Empty<RmsResponderExposure>())
				.Where(r => Same(r.UserId, memberUserId) && r.Status != (int)ResponderExposureStatuses.Dismissed)
				.ToList();
			await RevealAsync(departmentId, rows);
			Audit(departmentId, memberUserId, AuditLogTypes.ResponderExposuresExported, null, JsonConvert.SerializeObject(new { count = rows.Count }));
			return rows;
		}

		public async Task<object> BuildGdprExportAsync(string userId, int departmentId)
		{
			// Unattended: no grant, so a sealed value stays an envelope and the archive's sanitizer redacts it. Only the member's rows. Never gated
			// by Workforce Pro: the member's data access right holds whatever the add-on or the flag.
			var rows = (await _exposures.GetForUserAsync(departmentId, userId) ?? Enumerable.Empty<RmsResponderExposure>())
				.Where(r => Same(r.UserId, userId) && r.Status != (int)ResponderExposureStatuses.Dismissed)
				.ToList();
			return rows.Select(r => new
			{
				r.RmsResponderExposureId,
				Category = ((ResponderExposureCategories)r.Category).ToString(),
				Status = ((ResponderExposureStatuses)r.Status).ToString(),
				Source = ((ResponderExposureSources)r.Source).ToString(),
				r.OccurredOn,
				r.DurationMinutes,
				r.CallId,
				r.Substance,
				r.Narrative,
				r.PpeWorn,
				r.Decontamination,
				r.HealthDetails,
				r.RemindMember,
				r.RemindPeerTeam,
				r.ConfirmedOn,
				r.VoidedOn,
				r.CreatedOn
			}).ToList();
		}

		private async Task RequireWriterAsync(int departmentId, string actorUserId, string memberUserId)
		{
			if (string.IsNullOrWhiteSpace(actorUserId))
				throw new UnauthorizedAccessException("A signed-in member is required.");
			if (Same(actorUserId, memberUserId))
			{
				if (!await IsCurrentMemberAsync(departmentId, actorUserId))
					throw new UnauthorizedAccessException("Only a current member records their own entries.");
				return;
			}
			await RequireOfficerAsync(departmentId, actorUserId);
		}

		private async Task<RmsResponderExposure> LoadForWriteAsync(int departmentId, string actorUserId, string exposureId)
		{
			await RequireReadableAsync(departmentId);
			var row = string.IsNullOrWhiteSpace(exposureId) ? null : await _exposures.GetByIdForDepartmentAsync(departmentId, exposureId);
			if (row == null)
				throw new MemberWellnessException("entry_not_found");
			await RequireWriterAsync(departmentId, actorUserId, row.UserId);
			await RequireWritableForAsync(departmentId, row.UserId);
			return row;
		}

		private async Task ValidateAsync(int departmentId, ResponderExposureInput input)
		{
			if (!MemberWellnessRules.IsKnownCategory(input.Category))
				throw new MemberWellnessException("category_required");
			if (input.OccurredOn == default || input.OccurredOn > DateTime.UtcNow.AddDays(1) || input.OccurredOn < new DateTime(1950, 1, 1))
				throw new MemberWellnessException("date_invalid");
			if (input.DurationMinutes.HasValue && (input.DurationMinutes.Value < 0 || input.DurationMinutes.Value > 7 * 24 * 60))
				throw new MemberWellnessException("duration_invalid");
			if (input.CallId.HasValue)
			{
				var call = await _callsService.GetCallByIdAsync(input.CallId.Value);
				if (call == null || call.DepartmentId != departmentId)
					throw new MemberWellnessException("call_not_found");
			}
		}

		private static void Apply(RmsResponderExposure row, ResponderExposureInput input)
		{
			row.Category = input.Category;
			row.CallId = input.CallId;
			row.OccurredOn = DateTime.SpecifyKind(input.OccurredOn, DateTimeKind.Utc);
			row.DurationMinutes = input.DurationMinutes;
			row.Substance = Trim(input.Substance, MemberWellnessRules.SubstanceMax);
			row.Narrative = Trim(input.Narrative, MemberWellnessRules.NarrativeMax);
			row.PpeWorn = Trim(input.PpeWorn, MemberWellnessRules.PpeWornMax);
			row.Decontamination = Trim(input.Decontamination, MemberWellnessRules.DecontaminationMax);
			row.HealthDetails = Trim(input.HealthDetails, MemberWellnessRules.HealthDetailsMax);
		}

		private async Task ApplyRemindersAsync(int departmentId, RmsResponderExposure row, bool remindMember, bool remindPeerTeam, DateTime utcNow)
		{
			if (!row.Counts || !MemberWellnessRules.OffersAnniversaryReminders(row.Category) || (!remindMember && !remindPeerTeam))
			{
				ClearReminders(row);
				return;
			}

			row.RemindMember = remindMember;
			row.RemindPeerTeam = remindPeerTeam;
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			var localOccurred = department == null ? row.OccurredOn : DateTime.SpecifyKind(row.OccurredOn, DateTimeKind.Utc).TimeConverter(department);
			var localToday = department == null ? utcNow : DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).TimeConverter(department);
			row.NextAnniversaryOn = MemberWellnessRules.NextAnniversary(localOccurred, localToday);
		}

		private static void ClearReminders(RmsResponderExposure row)
		{
			row.RemindMember = false;
			row.RemindPeerTeam = false;
			row.NextAnniversaryOn = null;
		}

		private static string Trim(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var trimmed = value.Trim();
			return trimmed.Length > max ? trimmed.Substring(0, max) : trimmed;
		}

		private static RmsResponderExposure Clone(RmsResponderExposure row) => (RmsResponderExposure)row.GetType().GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(row, null);

		/// <summary>Audit detail: ids, category, status, dates and which protected fields hold a value. Never a protected value.</summary>
		internal static string Snapshot(RmsResponderExposure row)
		{
			if (row == null)
				return null;
			return JsonConvert.SerializeObject(new
			{
				id = row.RmsResponderExposureId,
				user = row.UserId,
				call = row.CallId,
				category = ((ResponderExposureCategories)row.Category).ToString(),
				status = ((ResponderExposureStatuses)row.Status).ToString(),
				occurred = row.OccurredOn,
				minutes = row.DurationMinutes,
				remind_member = row.RemindMember,
				remind_peer_team = row.RemindPeerTeam,
				details = WellnessProtectedFields.Exposures.Where(a => !string.IsNullOrEmpty(a.Value.Get(row))).Select(a => a.Key.Substring(a.Key.IndexOf('.') + 1)).ToList()
			});
		}

		#endregion Entries

		#region Protection

		private async Task ProtectAsync(int departmentId, RmsResponderExposure row, RmsResponderExposure existing, CancellationToken cancellationToken)
		{
			var result = await _protectedWrite.Value.PrepareRecordsEntityWriteAsync(departmentId, row, existing, row.RmsResponderExposureId, WellnessProtectedFields.Exposures,
				() => { row.IsProtected = true; row.ProtectedCatalogVersion = Math.Max(row.ProtectedCatalogVersion, WellnessProtectedFields.CatalogVersion); },
				null, null, true, cancellationToken);
			if (result != null && !result.Success)
				throw new MemberWellnessException("protected_write_refused");
		}

		private async Task ProtectAsync(int departmentId, RmsResponderScreeningFlag row, RmsResponderScreeningFlag existing, CancellationToken cancellationToken)
		{
			var result = await _protectedWrite.Value.PrepareRecordsEntityWriteAsync(departmentId, row, existing, row.RmsResponderScreeningFlagId, WellnessProtectedFields.ScreeningFlags,
				() => { row.IsProtected = true; row.ProtectedCatalogVersion = Math.Max(row.ProtectedCatalogVersion, WellnessProtectedFields.CatalogVersion); },
				null, null, true, cancellationToken);
			if (result != null && !result.Success)
				throw new MemberWellnessException("protected_write_refused");
		}

		/// <summary>Reveals for the caller's grant; true when something stayed withheld. Never throws (a failure withholds).</summary>
		private async Task<bool> RevealAsync(int departmentId, List<RmsResponderExposure> rows)
		{
			if (rows == null || rows.Count == 0)
				return false;
			try
			{
				var result = await _protectedRead.Value.ResolveRecordsEntitiesForReadAsync(departmentId, rows.Select(r => (r, r.RmsResponderExposureId)).ToList(),
					WellnessProtectedFields.Exposures, _grant?.GrantToken, _grant?.UserId);
				return result != null && result.RedactedFields != null && result.RedactedFields.Count > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Protected exposure entries could not be resolved for read.");
				foreach (var row in rows.Where(r => r.IsProtected))
					foreach (var accessor in WellnessProtectedFields.Exposures.Values)
						if (ProtectedDataEnvelope.HasEnvelopePrefix(accessor.Get(row)))
							accessor.Set(row, ProtectedDataEnvelope.RedactionValue);
				return true;
			}
		}

		private async Task<bool> RevealAsync(int departmentId, List<RmsResponderScreeningFlag> rows)
		{
			if (rows == null || rows.Count == 0)
				return false;
			try
			{
				var result = await _protectedRead.Value.ResolveRecordsEntitiesForReadAsync(departmentId, rows.Select(r => (r, r.RmsResponderScreeningFlagId)).ToList(),
					WellnessProtectedFields.ScreeningFlags, _grant?.GrantToken, _grant?.UserId);
				return result != null && result.RedactedFields != null && result.RedactedFields.Count > 0;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A protected screening plan could not be resolved for read.");
				foreach (var row in rows.Where(r => ProtectedDataEnvelope.HasEnvelopePrefix(r.PlanNotes)))
					row.PlanNotes = ProtectedDataEnvelope.RedactionValue;
				return true;
			}
		}

		#endregion Protection

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
	}
}
