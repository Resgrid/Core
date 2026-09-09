using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Localization;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public sealed partial class ChecklistReminderService : IChecklistReminderService
	{
		private readonly IChecklistRepository _store;
		private readonly IChecklistReminderRepository _reminders;
		private readonly IUnitOfWork _uow;
		private readonly IReadinessAccessService _access;
		private readonly IDepartmentsService _departments;
		private readonly IUnitsService _units;
		private readonly IDepartmentGroupsService _groups;
		private readonly IAuthorizationService _authorization;
		private readonly IChecklistAuthorizationService _checklistAuthorization;
		private readonly ICommunicationService _communication;
		private readonly IUserProfileService _profiles;
		private readonly IDepartmentSettingsService _settings;
		private readonly IChecklistAssignmentService _assignments;
		private readonly IChecklistAssetSource _assets;
		private static readonly ResourceManager Strings = new ResourceManager("Resgrid.Localization.Areas.User.Checklists.Checklists", typeof(SupportedLocales).Assembly);
		public ChecklistReminderService(IChecklistRepository store, IChecklistReminderRepository reminders, IUnitOfWork uow, IReadinessAccessService access,
			IDepartmentsService departments, IUnitsService units, IDepartmentGroupsService groups, IAuthorizationService authorization,
			IChecklistAuthorizationService checklistAuthorization, ICommunicationService communication, IUserProfileService profiles, IDepartmentSettingsService settings, IChecklistAssignmentService assignments = null, IChecklistAssetSource assets = null)
		{ _store = store; _reminders = reminders; _uow = uow; _access = access; _departments = departments; _units = units; _groups = groups; _authorization = authorization; _checklistAuthorization = checklistAuthorization; _communication = communication; _profiles = profiles; _settings = settings; _assignments = assignments; _assets = assets; }

		private async Task<T> TransactionAsync<T>(int departmentId, Func<Task<T>> action, CancellationToken ct)
		{
			if (_uow.Transaction != null) throw new InvalidOperationException("Checklist reminders own their transaction.");
			try { await _uow.CreateOrGetConnectionAsync(ct); await _store.LockDepartmentAsync(departmentId, ct); var result = await action(); _uow.CommitChanges(); return result; }
			catch { _uow.DiscardChanges(); throw; }
		}
		private async Task<DepartmentChecklistSettings> SettingsAsync(int departmentId, CancellationToken ct) => (await _store.ListAsync<DepartmentChecklistSettings>(departmentId, take: 1, ct: ct)).SingleOrDefault();
		public async Task<ChecklistReminderSweepResult> SweepAsync(DateTime utcNow, CancellationToken ct = default)
		{
			var origin = ChecklistRecurrence.Utc(utcNow); var elapsed = Stopwatch.StartNew(); var result = new ChecklistReminderSweepResult(); var after = 0;
			while (true)
			{
				var departments = await _reminders.DepartmentsAsync(after, ct); if (departments.Count == 0) break;
				foreach (var departmentId in departments)
				{
					ct.ThrowIfCancellationRequested(); after = departmentId;
					try
					{
						await GenerateAsync(departmentId, origin.Add(elapsed.Elapsed), ct);
						await GenerateTimedAsync(departmentId, origin.Add(elapsed.Elapsed), ct);
						// Bound work per tenant. Remaining notices are retained for the next sweep.
						for (var batch = 0; batch < 100; batch++)
						{
							var now = origin.Add(elapsed.Elapsed);
							var claimed = await TransactionAsync(departmentId, async () => await _reminders.ClaimAsync(departmentId, now, (await SettingsAsync(departmentId, ct))?.DigestMode ?? true, ct), ct);
							if (claimed.Count == 0) break;
							await DeliverAsync(departmentId, claimed, now, result, ct);
						}
					}
					catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
					catch { result.Errors++; } // Never persist provider exceptions or sensitive content.
				}
			}
			return result;
		}
		private async Task GenerateAsync(int departmentId, DateTime now, CancellationToken ct)
		{
			if (!await _access.CanUseChecklistsAsync(departmentId)) return;
			for (var skip = 0; ; skip += 50)
			{
				var count = await TransactionAsync(departmentId, async () =>
				{
					var settings = await SettingsAsync(departmentId, ct);
					if (settings?.RemindersEnabled != true) return 0;
					var rows = await _store.DueOccurrencesAsync(departmentId, now, skip, ct);
					foreach (var row in rows)
					{
						ct.ThrowIfCancellationRequested();
						if (!await EligibleOccurrenceAsync(row, settings, ct)) continue;
						foreach (var kind in new[] { ChecklistReminderKind.Due, ChecklistReminderKind.Missed, ChecklistReminderKind.Escalation })
						{
							if (!EligibleKind(row, settings, kind, now)) continue;
							foreach (var user in await RecipientsAsync(row, kind))
								await _reminders.EnqueueAsync(new ChecklistReminder { DepartmentId = departmentId, OccurrenceId = row.Id, RecipientUserId = user, Kind = (int)kind, CreatedOnUtc = now, NextAttemptUtc = now }, ct);
						}
					}
					return rows.Count;
				}, ct);
				if (count < 50) break;
			}
		}
		private async Task<bool> EligibleOccurrenceAsync(ChecklistOccurrence row, DepartmentChecklistSettings settings, CancellationToken ct)
		{
			if (settings?.RemindersEnabled != true || !settings.RemindersActiveFromUtc.HasValue || row == null || row.DepartmentId != settings.DepartmentId || row.ScheduleId == null
				|| !row.PeriodStartUtc.HasValue || !row.WindowEndUtc.HasValue || row.PeriodStartUtc < settings.RemindersActiveFromUtc || row.State != 0 && row.State != 1 && row.State != 3 && row.State != 4) return false;
			var schedule = await _store.GetAsync<ChecklistSchedule>(row.DepartmentId, row.ScheduleId, ct);
			if (schedule?.IsActive != true || schedule.IsSuspended || row.PeriodStartUtc < schedule.ActiveFromUtc) return false;
			var definition = await _store.GetAsync<ChecklistDefinition>(row.DepartmentId, row.ParentId, ct);
			return definition != null && !definition.Retired && !definition.DeletedOn.HasValue;
		}
		private static bool EligibleKind(ChecklistOccurrence row, DepartmentChecklistSettings settings, ChecklistReminderKind kind, DateTime now) => kind switch
		{
			ChecklistReminderKind.Due => row.State != 4 && settings.NotifyBeforeMinutes > 0 && row.WindowEndUtc > now && row.WindowEndUtc <= now.AddMinutes(settings.NotifyBeforeMinutes),
			ChecklistReminderKind.Missed => row.State == 4 && settings.NotifyMissed,
			ChecklistReminderKind.Escalation => row.State == 4 && settings.EscalateAfterMinutes.HasValue && row.WindowEndUtc <= now.AddMinutes(-settings.EscalateAfterMinutes.Value),
			ChecklistReminderKind.ShiftStart => settings.NotifyAtShiftStart && !settings.FixedDigestMinute.HasValue && row.PeriodStartUtc <= now,
			ChecklistReminderKind.FixedTime => settings.FixedDigestMinute.HasValue && row.PeriodStartUtc <= now,
			_ => false
		};
		private async Task<HashSet<string>> RecipientsAsync(ChecklistOccurrence row, ChecklistReminderKind kind)
		{
			if (row.TargetType == (int)ChecklistTargetType.InventoryAsset)
			{
				if (_assets == null || !await _assets.IsAvailableAsync(row.DepartmentId)) return new HashSet<string>();
				var asset = await _assets.RoutingAsync(row.DepartmentId, row.TargetId);
				if (asset?.DepartmentId != row.DepartmentId || asset.Id != row.TargetId) return new HashSet<string>();
				var routing = new ChecklistOccurrence { DepartmentId = row.DepartmentId, ScheduleId = row.ScheduleId, TargetType = asset.UnitId.HasValue ? 1 : asset.UserId != null ? 3 : asset.GroupId.HasValue ? 2 : 0,
					TargetId = asset.UnitId?.ToString(CultureInfo.InvariantCulture) ?? asset.UserId ?? asset.GroupId?.ToString(CultureInfo.InvariantCulture) ?? row.DepartmentId.ToString(CultureInfo.InvariantCulture) };
				var recipients = await RecipientsAsync(routing, kind);
				foreach (var user in recipients.ToArray())
					if (!await _assets.CanReceiveReminderAsync(row.DepartmentId, user, row.TargetId)) recipients.Remove(user);
				return recipients;
			}
			var department = await _departments.GetDepartmentByIdAsync(row.DepartmentId, true);
			if (department == null) return new HashSet<string>();
			var members = (await _departments.GetAllMembersForDepartmentUnlimitedAsync(row.DepartmentId, true))
				.Where(m => m.DepartmentId == row.DepartmentId && !m.IsDeleted && m.IsDisabled != true && !string.IsNullOrWhiteSpace(m.UserId)).ToList();
			var allowed = members.Select(m => m.UserId).ToHashSet(StringComparer.Ordinal);
			var admins = members.Where(m => m.IsAdmin == true || m.UserId == department.ManagingUserId).Select(m => m.UserId).ToHashSet(StringComparer.Ordinal);
			var users = new HashSet<string>(StringComparer.Ordinal);
			Unit unit = null; DepartmentGroup group = null;
			if (row.TargetType == (int)ChecklistTargetType.Unit && int.TryParse(row.TargetId, out var unitId))
			{
				unit = await _units.GetUnitByIdAsync(unitId); if (unit?.DepartmentId != row.DepartmentId) return users;
				users.UnionWith((await _units.GetActiveRolesForUnitAsync(unitId)).Where(r => r.DepartmentId == row.DepartmentId && r.UnitId == unitId && allowed.Contains(r.UserId)).Select(r => r.UserId));
				if (users.Count == 0 && unit.StationGroupId.HasValue)
				{
					group = await _groups.GetGroupByIdAsync(unit.StationGroupId.Value, true);
					if (group?.DepartmentId == row.DepartmentId) users.UnionWith((await _groups.GetAllMembersForGroupAsync(group.DepartmentGroupId)).Where(m => m.DepartmentId == row.DepartmentId).Select(m => m.UserId));
				}
				users.IntersectWith(allowed); if (users.Count == 0) users.UnionWith(admins);
			}
			else if (row.TargetType == (int)ChecklistTargetType.Group && int.TryParse(row.TargetId, out var groupId))
			{
				group = await _groups.GetGroupByIdAsync(groupId, true); if (group?.DepartmentId != row.DepartmentId) return users;
				users.UnionWith((await _groups.GetAllMembersForGroupAsync(groupId)).Where(m => m.DepartmentId == row.DepartmentId).Select(m => m.UserId));
			}
			else if (row.TargetType == (int)ChecklistTargetType.Personnel) { if (!allowed.Contains(row.TargetId)) return users; users.Add(row.TargetId); }
			else if (row.TargetType == (int)ChecklistTargetType.Department && row.TargetId == row.DepartmentId.ToString(CultureInfo.InvariantCulture)) users.UnionWith(admins);
			else return users;
			var schedule = row.ScheduleId == null ? null : await _store.GetAsync<ChecklistSchedule>(row.DepartmentId, row.ScheduleId);
			if (schedule?.AssignmentType > 0) users = _assignments == null ? new HashSet<string>() : await _assignments.MembersAsync(row.DepartmentId, schedule.AssignmentType, schedule.AssignmentId);
			if (kind == ChecklistReminderKind.Escalation) users = admins;
			users.IntersectWith(allowed);
			foreach (var user in users.ToArray())
			{
				if (unit != null && !await _authorization.CanUserViewUnitAsync(user, unit.UnitId)
					|| row.TargetType == (int)ChecklistTargetType.Personnel && user != row.TargetId && !await _authorization.CanUserViewPersonAsync(user, row.TargetId, row.DepartmentId)
					|| row.TargetType == (int)ChecklistTargetType.Group && (await _groups.GetGroupForUserAsync(user, row.DepartmentId))?.DepartmentGroupId != group.DepartmentGroupId
						&& !await _checklistAuthorization.CanManageAsync(new ChecklistActor { DepartmentId = row.DepartmentId, UserId = user })) users.Remove(user);
			}
			return users;
		}
		private async Task DeliverAsync(int departmentId, List<ChecklistReminder> claimed, DateTime now, ChecklistReminderSweepResult result, CancellationToken ct)
		{
			var eligible = new List<ChecklistReminder>(); var elapsed = Stopwatch.StartNew();
			try
			{
				var settings = await SettingsAsync(departmentId, ct); var enabled = await _access.CanUseChecklistsAsync(departmentId);
				foreach (var notice in claimed)
				{
					ct.ThrowIfCancellationRequested();
					var row = enabled ? await _store.GetAsync<ChecklistOccurrence>(departmentId, notice.OccurrenceId, ct) : null;
					if (enabled && await EligibleOccurrenceAsync(row, settings, ct) && EligibleKind(row, settings, (ChecklistReminderKind)notice.Kind, now)
						&& await TimedRecipientAsync(row, notice, settings, now, ct) && (await RecipientsAsync(row, (ChecklistReminderKind)notice.Kind)).Contains(notice.RecipientUserId)) eligible.Add(notice);
					else { await FinishAsync(notice, ChecklistReminderStatus.Suppressed, now, ct); result.Suppressed++; }
				}
				if (eligible.Count == 0) return;
				var user = eligible[0].RecipientUserId;
				var profile = await _profiles.GetProfileByUserIdAsync(user, false);
				var culture = Culture(profile?.Language);
				var department = await _departments.GetDepartmentByIdAsync(departmentId, true);
				var number = await _settings.GetTextToCallNumberForDepartmentAsync(departmentId);
				ct.ThrowIfCancellationRequested();
				// Profile/routing lookups can be slow. Revalidate after those calls, including edits
				// to timing controls, and leave room in the lease before beginning provider handoff.
				settings = await SettingsAsync(departmentId, ct);
				var enabledNow = await _access.CanUseChecklistsAsync(departmentId);
				foreach (var notice in eligible.ToArray())
				{
					var row = enabledNow ? await _store.GetAsync<ChecklistOccurrence>(departmentId, notice.OccurrenceId, ct) : null;
					if (!enabledNow || !await EligibleOccurrenceAsync(row, settings, ct) || !EligibleKind(row, settings, (ChecklistReminderKind)notice.Kind, now.Add(elapsed.Elapsed))
						|| !await TimedRecipientAsync(row, notice, settings, now.Add(elapsed.Elapsed), ct) || !(await RecipientsAsync(row, (ChecklistReminderKind)notice.Kind)).Contains(user))
					{ await FinishAsync(notice, ChecklistReminderStatus.Suppressed, now.Add(elapsed.Elapsed), ct); eligible.Remove(notice); result.Suppressed++; }
				}
				if (eligible.Count == 0) return;
				if (elapsed.Elapsed >= TimeSpan.FromMinutes(25)) throw new InvalidOperationException("Checklist reminder lease is expiring.");
				// Generic copy and an authenticated link only; this worker never resolves protected content.
				var handedOff = enabledNow && await _communication.SendNotificationAsync(user, departmentId,
					Strings.GetString("ReminderMessage", culture) + " " + (Config.SystemBehaviorConfig.ResgridBaseUrl ?? "").TrimEnd('/') + "/User/Checklists/Due",
					number, department, Strings.GetString("ReminderTitle", culture), profile);
				foreach (var notice in eligible) { await FinishAsync(notice, handedOff ? ChecklistReminderStatus.HandedOff : ChecklistReminderStatus.Suppressed, now.Add(elapsed.Elapsed), ct); if (handedOff) result.HandedOff++; else result.Suppressed++; }
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch
			{
				// Leases also recover process crashes. Provider handoff is at-least-once; the shared
				// communication service does not expose delivery receipts or idempotency keys.
				foreach (var notice in claimed) { try { await FinishAsync(notice, ChecklistReminderStatus.Pending, now.Add(elapsed.Elapsed), ct); } catch { /* A completed or superseded claim must never be reset. */ } }
				result.Errors++;
			}
		}
		private Task<bool> FinishAsync(ChecklistReminder notice, ChecklistReminderStatus status, DateTime now, CancellationToken ct) => TransactionAsync(notice.DepartmentId, async () => { await _reminders.FinishAsync(notice, status, now, ct); return true; }, ct);
		private static CultureInfo Culture(string language)
		{
			try { var culture = CultureInfo.GetCultureInfo(language ?? "en"); if (SupportedLocales.GetSupportedCultures().Contains(culture.TwoLetterISOLanguageName)) return culture; }
			catch (CultureNotFoundException) { }
			return CultureInfo.GetCultureInfo("en");
		}
	}
}
