using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Checklists;

namespace Resgrid.Services
{
	public sealed partial class ChecklistReminderService
	{
		private async Task<bool> TimedRecipientAsync(ChecklistOccurrence row, ChecklistReminder notice, DepartmentChecklistSettings settings, DateTime now, CancellationToken ct)
		{
			if (notice.Kind < (int)ChecklistReminderKind.ShiftStart) return true;
			var floor = new[] { settings.DigestActiveFromUtc ?? settings.RemindersActiveFromUtc.Value, settings.RemindersActiveFromUtc.Value, now.AddDays(-1) }.Max();
			if (notice.CreatedOnUtc < floor) return false;
			if (notice.Kind == (int)ChecklistReminderKind.FixedTime)
			{
				if (!notice.PeriodKey.StartsWith("fixed:", StringComparison.Ordinal) || !DateTime.TryParseExact(notice.PeriodKey.Substring(6), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return false;
				var department = await _departments.GetDepartmentByIdAsync(row.DepartmentId, true);
				var at = ChecklistRecurrence.ToUtc(day.AddMinutes(settings.FixedDigestMinute.Value), ChecklistRecurrence.Zone(department?.TimeZone ?? "UTC"));
				return at > floor && at <= now;
			}
			var parts = notice.PeriodKey.Split(':');
			if (parts.Length != 3 || parts[0] != "shift" || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var unitId)) return false;
			if (!(await _store.ShiftStartsAsync(row.DepartmentId, floor, now, ct)).Any(s => s.WorkshiftDayId == parts[1] && s.UnitId == unitId)) return false;
			var unit = await _units.GetUnitByIdAsync(unitId); if (unit?.DepartmentId != row.DepartmentId) return false;
			var crew = await ShiftCrewAsync(unit);
			return crew.Contains(notice.RecipientUserId) && await AppliesToShiftAsync(row, unit, crew);
		}
		private async Task<HashSet<string>> ShiftCrewAsync(Unit unit)
		{
			var crew = (await _units.GetActiveRolesForUnitAsync(unit.UnitId)).Where(r => r.DepartmentId == unit.DepartmentId && r.UnitId == unit.UnitId && !string.IsNullOrWhiteSpace(r.UserId)).Select(r => r.UserId).ToHashSet(StringComparer.Ordinal);
			if (crew.Count == 0 && unit.StationGroupId.HasValue && (await _groups.GetGroupByIdAsync(unit.StationGroupId.Value, true))?.DepartmentId == unit.DepartmentId)
				crew.UnionWith((await _groups.GetAllMembersForGroupAsync(unit.StationGroupId.Value)).Where(m => m.DepartmentId == unit.DepartmentId).Select(m => m.UserId));
			return crew;
		}
		private async Task GenerateTimedAsync(int departmentId, DateTime now, CancellationToken ct)
		{
			if (!await _access.CanUseChecklistsAsync(departmentId)) return;
			await TransactionAsync(departmentId, async () =>
			{
				var settings = await SettingsAsync(departmentId, ct);
				if (settings?.RemindersEnabled != true || !settings.RemindersActiveFromUtc.HasValue) return false;
				// Reconcile the last day on every sweep. Unique recipient/trigger identities suppress replays
				// while current asset moves and staffing changes can reach their new recipients.
				var from = new[] { settings.DigestActiveFromUtc ?? settings.RemindersActiveFromUtc.Value, settings.RemindersActiveFromUtc.Value, now.AddDays(-1) }.Max();
				if (from >= now) return false;
				var triggers = new List<(string Key, ChecklistReminderKind Kind, int? UnitId, DateTime At)>();
				if (settings.FixedDigestMinute.HasValue)
				{
					var department = await _departments.GetDepartmentByIdAsync(departmentId, true);
					var zone = ChecklistRecurrence.Zone(department?.TimeZone ?? "UTC");
					var last = TimeZoneInfo.ConvertTimeFromUtc(ChecklistRecurrence.Utc(now), zone).Date;
					for (var day = TimeZoneInfo.ConvertTimeFromUtc(ChecklistRecurrence.Utc(from), zone).Date; day <= last; day = day.AddDays(1))
					{
						var at = ChecklistRecurrence.ToUtc(day.AddMinutes(settings.FixedDigestMinute.Value), zone);
						if (at > from && at <= now) triggers.Add(("fixed:" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture), ChecklistReminderKind.FixedTime, null, at));
					}
				}
				else if (settings.NotifyAtShiftStart)
				{
					foreach (var shift in await _store.ShiftStartsAsync(departmentId, from, now, ct))
						triggers.Add(("shift:" + shift.WorkshiftDayId + ":" + shift.UnitId.ToString(CultureInfo.InvariantCulture), ChecklistReminderKind.ShiftStart, shift.UnitId, ChecklistRecurrence.Utc(shift.StartUtc)));
				}
				if (triggers.Count > 0)
					for (var skip = 0; ; skip += 50)
					{
						var occurrences = await _store.DueOccurrencesAsync(departmentId, now, skip, ct);
						foreach (var row in occurrences)
						{
							if (!await EligibleOccurrenceAsync(row, settings, ct)) continue;
							foreach (var trigger in triggers.Where(t => row.PeriodStartUtc <= t.At))
							{
								HashSet<string> crew = null;
								if (trigger.UnitId.HasValue)
								{
									var unit = await _units.GetUnitByIdAsync(trigger.UnitId.Value); if (unit?.DepartmentId != departmentId) continue;
									crew = await ShiftCrewAsync(unit);
									if (!await AppliesToShiftAsync(row, unit, crew)) continue;
								}
								var recipients = await RecipientsAsync(row, trigger.Kind); if (crew != null) recipients.IntersectWith(crew);
								foreach (var user in recipients)
									await _reminders.EnqueueAsync(new ChecklistReminder { DepartmentId = departmentId, OccurrenceId = row.Id, RecipientUserId = user, Kind = (int)trigger.Kind, PeriodKey = trigger.Key, CreatedOnUtc = now, NextAttemptUtc = now }, ct);
							}
						}
						if (occurrences.Count < 50) break;
					}
				await _store.AdvanceDigestSweepAsync(departmentId, now, ct); return true;
			}, ct);
		}
		private async Task<bool> AppliesToShiftAsync(ChecklistOccurrence row, Unit unit, HashSet<string> crew)
		{
			if (row.TargetType == 0) return true;
			if (row.TargetType == 1) return row.TargetId == unit.UnitId.ToString(CultureInfo.InvariantCulture);
			if (row.TargetType == 2) return row.TargetId == unit.StationGroupId?.ToString(CultureInfo.InvariantCulture);
			if (row.TargetType == 3) return crew.Contains(row.TargetId);
			if (row.TargetType != 5 || _assets == null || !await _assets.IsAvailableAsync(row.DepartmentId)) return false;
			var asset = await _assets.RoutingAsync(row.DepartmentId, row.TargetId);
			return asset?.DepartmentId == row.DepartmentId && asset.Id == row.TargetId && (asset.UnitId == unit.UnitId || asset.UserId != null && crew.Contains(asset.UserId) || asset.GroupId.HasValue && asset.GroupId == unit.StationGroupId);
		}
	}
}
