using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Checklists;

namespace Resgrid.Services
{
	public partial class ChecklistsService
	{
		public async Task<bool> AssetTargetsAvailableAsync(ChecklistActor actor) { await _authorization.RequireMemberAsync(actor); return _assets != null && await _assets.IsAvailableAsync(actor.DepartmentId); }
		public async Task<List<ChecklistAssignmentChoice>> AssignmentChoicesAsync(ChecklistActor actor)
		{ await RequireWriteAsync(actor, true); return _assignments == null ? new List<ChecklistAssignmentChoice>() : await _assignments.ChoicesAsync(actor); }
		private Task<bool> CanPerformScheduleAsync(ChecklistActor actor, ChecklistSchedule schedule) => schedule == null ? Task.FromResult(false)
			: schedule.AssignmentType == 0 ? Task.FromResult(true) : _assignments?.CanPerformAsync(actor, schedule) ?? Task.FromResult(false);
		private async Task RequireRunAssignmentAsync(ChecklistActor actor, ChecklistCompletion run)
		{
			var occurrence = await _store.GetAsync<ChecklistOccurrence>(actor.DepartmentId, run.OccurrenceId);
			if (occurrence?.ScheduleId != null && !await CanPerformScheduleAsync(actor, await _store.GetAsync<ChecklistSchedule>(actor.DepartmentId, occurrence.ScheduleId)))
				throw new ChecklistException(403, "AssignmentUnavailable");
		}
		public async Task<ChecklistOccurrenceView> OccurrenceAsync(ChecklistActor actor, string id)
		{
			Id(id); await _authorization.RequireMemberAsync(actor);
			var occurrence = await _store.GetAsync<ChecklistOccurrence>(actor.DepartmentId, id);
			if (occurrence?.ScheduleId == null) throw new ChecklistException(404, "OccurrenceUnavailable");
			var target = await _authorization.TargetAsync(actor, (ChecklistTargetType)occurrence.TargetType, occurrence.TargetId);
			var schedule = await RevealAsync(actor, await _store.GetAsync<ChecklistSchedule>(actor.DepartmentId, occurrence.ScheduleId));
			var enabled = await _access.CanUseChecklistsAsync(actor.DepartmentId); occurrence.Content = null;
			return new ChecklistOccurrenceView { Occurrence = occurrence, Name = Decode<ChecklistScheduleContent>(schedule.Content).Name, Target = target,
				CanStart = enabled && schedule.IsActive && !schedule.IsSuspended && await CanPerformScheduleAsync(actor, schedule) && new[] { 0, 1, 3, 4 }.Contains(occurrence.State) && occurrence.PeriodStartUtc <= _clock.GetUtcNow().UtcDateTime,
				CanSkip = enabled && await CanManageAsync(actor) && new[] { 3, 4 }.Contains(occurrence.State) && await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, occurrence.CompletionId) == null };
		}
		public async Task<List<ChecklistCalendarEntry>> CalendarAsync(ChecklistActor actor, DateTime fromUtc, DateTime untilUtc)
		{
			await _authorization.RequireMemberAsync(actor);
			fromUtc = ChecklistRecurrence.Utc(fromUtc); untilUtc = ChecklistRecurrence.Utc(untilUtc);
			if (untilUtc <= fromUtc || (untilUtc - fromUtc).TotalDays > 93) throw new ChecklistException(400, "CalendarRange");
			var result = new List<ChecklistCalendarEntry>();
			if (!await _access.CanUseChecklistsAsync(actor.DepartmentId)) return result;
			var names = new Dictionary<string, (string Name, bool Redacted)>();
			var targets = new Dictionary<(int Type, string Id), bool>();
			for (var skip = 0; ; skip += 500)
			{
				var rows = await _store.CalendarOccurrencesAsync(actor.DepartmentId, fromUtc, untilUtc, skip);
				foreach (var row in rows)
				{
					var key = (row.TargetType, row.TargetId);
					if (!targets.TryGetValue(key, out var allowed))
					{
						try { await _authorization.TargetAsync(actor, (ChecklistTargetType)row.TargetType, row.TargetId); allowed = true; }
						catch (ChecklistException ex) when (ex.StatusCode == 403 || ex.StatusCode == 404) { allowed = false; }
						targets[key] = allowed;
					}
					if (!allowed) continue;
					if (!names.TryGetValue(row.ScheduleId, out var name))
					{
						var schedule = await _store.GetAsync<ChecklistSchedule>(actor.DepartmentId, row.ScheduleId);
						if (schedule == null) continue;
						try { name = (Decode<ChecklistScheduleContent>((await RevealAsync(actor, schedule)).Content).Name, false); }
						catch (ChecklistException ex) when (ex.StatusCode == 403) { name = (ProtectedDataEnvelope.RedactionValue, true); }
						names[row.ScheduleId] = name;
					}
					result.Add(new ChecklistCalendarEntry { Id = "checklist:" + row.Id, OccurrenceId = row.Id, Title = name.Name, IsRedacted = name.Redacted,
						StartUtc = ChecklistRecurrence.Utc(row.PeriodStartUtc.Value), EndUtc = ChecklistRecurrence.Utc(row.WindowEndUtc.Value), State = row.State });
				}
				if (rows.Count < 500) break;
			}
			return result;
		}
	}
}
