using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Checklists;

namespace Resgrid.Services
{
	public partial class ChecklistsService
	{
		public async Task<List<ChecklistScheduleView>> SchedulesAsync(ChecklistActor actor, string definitionId, int page = 0)
		{
			Id(definitionId); await _authorization.RequireMemberAsync(actor);
			if (!await CanManageAsync(actor)) throw new ChecklistException(403, "SchedulePermission");
			if (page < 0 || page > 10000) throw new ChecklistException(400, "ScheduleValidation");
			var views = new List<ChecklistScheduleView>();
			foreach (var row in await _store.ListAsync<ChecklistSchedule>(actor.DepartmentId, definitionId, page * 50, 50))
			{
				await RevealAsync(actor, row); views.Add(new ChecklistScheduleView { Schedule = row, Content = Decode<ChecklistScheduleContent>(row.Content) });
			}
			return views;
		}
		public async Task<ChecklistScheduleView> GetScheduleAsync(ChecklistActor actor, string id)
		{
			Id(id); await _authorization.RequireMemberAsync(actor);
			if (!await CanManageAsync(actor)) throw new ChecklistException(403, "SchedulePermission");
			var row = await RevealAsync(actor, await _store.GetAsync<ChecklistSchedule>(actor.DepartmentId, id));
			return new ChecklistScheduleView { Schedule = row, Content = Decode<ChecklistScheduleContent>(row.Content) };
		}
		public async Task<string> SaveScheduleAsync(ChecklistActor actor, ChecklistScheduleInput input)
		{
			await RequireWriteAsync(actor, true);
			if (input == null) throw new ChecklistException(400, "ScheduleValidation");
			Id(input.DefinitionId); if (input.Id != null) Id(input.Id);
			if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || input.Notes?.Length > 4000 || !Enum.IsDefined(input.Frequency) || input.Frequency == ChecklistScheduleFrequency.OnDemand
				|| input.WindowMinutes < 1 || input.WindowMinutes > 10080 || input.DayOfMonth < 1 || input.DayOfMonth > 31 || input.MonthOfYear < 1 || input.MonthOfYear > 12 || input.Weekdays < 1 || input.Weekdays > 127
				|| input.StartDate.Year < 2000 || input.StartDate.Year > 2200 || input.EndDate?.Date < input.StartDate.Date || input.EndDate?.Year > 2200)
				throw new ChecklistException(400, "ScheduleValidation");
			var zone = ChecklistRecurrence.Zone(input.TimeZoneId); var times = ChecklistRecurrence.ParseTimes(input.TimesOfDay);
			var workshift = string.IsNullOrWhiteSpace(input.WorkshiftId) ? null : input.WorkshiftId;
			if (workshift != null) { Id(workshift); if (input.Frequency != ChecklistScheduleFrequency.PerShift || !await _store.WorkshiftExistsAsync(actor.DepartmentId, workshift)) throw new ChecklistException(400, "ScheduleValidation"); }
			return await TransactionAsync(actor, async events =>
			{
				if (input.AssignmentType != 0 || !string.IsNullOrEmpty(input.AssignmentId))
				{ if (_assignments == null) throw new ChecklistException(400, "AssignmentUnavailable"); await _assignments.ValidateAsync(actor.DepartmentId, input.AssignmentType, input.AssignmentId); }
				var definition = await _store.GetAsync<ChecklistDefinition>(actor.DepartmentId, input.DefinitionId);
				if (definition?.CurrentVersionId == null || definition.DeletedOn.HasValue || definition.Retired && input.IsActive) throw new ChecklistException(409, "ScheduleRequiresPublished");
				var version = await RevealAsync(actor, await _store.GetAsync<ChecklistDefinitionVersion>(actor.DepartmentId, definition.CurrentVersionId));
				var target = await _authorization.TargetAsync(actor, Decode<ChecklistForm>(version.Content).TargetType, input.TargetId);
				var now = _clock.GetUtcNow().UtcDateTime; var insert = input.Id == null;
				var row = insert ? New<ChecklistSchedule>(actor, definition.Id) : await RevealAsync(actor, await _store.GetAsync<ChecklistSchedule>(actor.DepartmentId, input.Id));
				if (row.ParentId != definition.Id) throw new ChecklistException(404, "ScheduleUnavailable");
				if (!insert) { Revision(row, input.Revision); await _store.CancelUnstartedOccurrencesAsync(actor.DepartmentId, row.Id, input.IsActive ? now : null, now); }
				if (insert || !row.IsActive || row.IsSuspended) row.ActiveFromUtc = now;
				if (!insert) row.Revision++;
				row.VersionId = version.Id; row.TargetType = (int)target.Type; row.TargetId = target.Id; row.TargetGroupId = target.GroupId;
				row.AssignmentType = input.AssignmentType; row.AssignmentId = input.AssignmentId;
				row.Frequency = (int)input.Frequency; row.TimeZoneId = zone.Id; row.StartDate = input.StartDate.Date; row.EndDate = input.EndDate?.Date;
				row.ClockMinutes = string.Join(",", times); row.Weekdays = input.Weekdays; row.DayOfMonth = input.DayOfMonth; row.MonthOfYear = input.MonthOfYear;
				row.WindowMinutes = input.WindowMinutes; row.WorkshiftId = workshift; row.IsActive = input.IsActive; row.IsSuspended = false; row.GeneratedThroughUtc = now; row.LastSweepUtc = now;
				row.Content = JsonConvert.SerializeObject(new ChecklistScheduleContent { Name = input.Name.Trim(), Notes = input.Notes });
				await PersistAsync(actor, row, insert); await AuditAsync(actor, row, insert ? AuditLogTypes.ChecklistScheduleAdded : AuditLogTypes.ChecklistScheduleUpdated);
				await ScheduleEventAsync(row, null, WorkflowTriggerEventType.ChecklistScheduleChanged, events, now);
				return row.Id;
			}, accessFence: true);
		}
		public async Task<List<ChecklistOccurrenceView>> DueAsync(ChecklistActor actor, int page = 0, bool includeNext = false)
		{
			await _authorization.RequireMemberAsync(actor); if (page < 0 || page > 100) throw new ChecklistException(400, "ScheduleValidation");
			var enabled = await _access.CanUseChecklistsAsync(actor.DepartmentId); var manage = await CanManageAsync(actor); var views = new List<ChecklistOccurrenceView>();
			var remaining = page * 50; var take = includeNext ? 51 : 50;
			var until = _clock.GetUtcNow().UtcDateTime.AddDays(7);
			for (var skip = 0; ; skip += 50)
			{
				var rows = await _store.DueOccurrencesAsync(actor.DepartmentId, until, skip);
				foreach (var occurrence in rows)
				{
					ChecklistTarget target;
					try { target = await _authorization.TargetAsync(actor, (ChecklistTargetType)occurrence.TargetType, occurrence.TargetId); }
					catch (ChecklistException ex) when (ex.StatusCode == 404) { continue; }
					var schedule = await _store.GetAsync<ChecklistSchedule>(actor.DepartmentId, occurrence.ScheduleId);
					if (schedule == null) continue;
					if (remaining > 0) { remaining--; continue; }
					var assigned = await CanPerformScheduleAsync(actor, schedule);
					// The response omits stored Content/envelopes. Names come through the attended read boundary.
					await RevealAsync(actor, schedule);
					var metadata = JsonConvert.DeserializeObject<ChecklistOccurrence>(JsonConvert.SerializeObject(occurrence)); metadata.Content = null;
					views.Add(new ChecklistOccurrenceView { Occurrence = metadata, Name = Decode<ChecklistScheduleContent>(schedule.Content).Name, Target = target,
						CanStart = enabled && assigned && schedule.IsActive && !schedule.IsSuspended && occurrence.PeriodStartUtc <= _clock.GetUtcNow().UtcDateTime, CanSkip = enabled && manage && (occurrence.State == 3 || occurrence.State == 4) && await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, occurrence.CompletionId) == null });
					if (views.Count == take) return views;
				}
				if (rows.Count < 50) return views;
			}
		}
		public Task<string> StartOccurrenceAsync(ChecklistActor actor, string occurrenceId) => StartOccurrenceWithIdAsync(actor, occurrenceId, null);
		public async Task<string> StartOccurrenceWithIdAsync(ChecklistActor actor, string occurrenceId, string completionId)
		{
			Id(occurrenceId); await RequireWriteAsync(actor);
			occurrenceId = Guid.Parse(occurrenceId).ToString();
			if (completionId != null) { Id(completionId); completionId = Guid.Parse(completionId).ToString(); }
			return await TransactionAsync(actor, async events =>
			{
				var row = await _store.GetAsync<ChecklistOccurrence>(actor.DepartmentId, occurrenceId);
				if (row?.ScheduleId == null || row.State == (int)ChecklistOccurrenceState.Skipped || row.State == (int)ChecklistOccurrenceState.Cancelled || row.PeriodStartUtc > _clock.GetUtcNow().UtcDateTime) throw new ChecklistException(409, "OccurrenceUnavailable");
				var existing = await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, row.CompletionId);
				var schedule = await _store.GetAsync<ChecklistSchedule>(actor.DepartmentId, row.ScheduleId);
				if (!await CanPerformScheduleAsync(actor, schedule)) throw new ChecklistException(403, "AssignmentPermission");
				if (existing != null)
				{
					await RequireRunReadAsync(actor, existing);
					if (completionId != null && (completionId != existing.Id || existing.CreatedBy != actor.UserId)) throw new ChecklistException(409, "Run identifier is already in use.");
					await RevealAsync(actor, existing); return existing.Id;
				}
				if (completionId != null)
				{
					if (await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, completionId) != null) throw new ChecklistException(409, "Run identifier is already in use.");
					row.CompletionId = completionId;
				}
				var definition = await _store.GetAsync<ChecklistDefinition>(actor.DepartmentId, row.ParentId);
				if (schedule?.IsActive != true || schedule.IsSuspended || definition == null || definition.Retired || definition.DeletedOn.HasValue) throw new ChecklistException(409, "OccurrenceUnavailable");
				var target = await _authorization.TargetAsync(actor, (ChecklistTargetType)row.TargetType, row.TargetId);
				var version = await RevealAsync(actor, await _store.GetAsync<ChecklistDefinitionVersion>(actor.DepartmentId, row.VersionId));
				if (version.ParentId != row.ParentId) throw new ChecklistException(409, "OccurrenceUnavailable");
				// Never copy the schedule/version envelope to another AAD identity. Capture the target with this actor's grant.
				row.Content = JsonConvert.SerializeObject(target); row.State = row.MissedOn.HasValue ? (int)ChecklistOccurrenceState.Missed : (int)ChecklistOccurrenceState.InProgress; row.Revision++;
				await PersistAsync(actor, row, false);
				var completion = New<ChecklistCompletion>(actor, row.ParentId); completion.Id = row.CompletionId;
				completion.OccurrenceId = row.Id; completion.VersionId = row.VersionId; completion.TargetType = row.TargetType; completion.TargetId = row.TargetId; completion.TargetGroupId = target.GroupId;
				completion.Content = "{}"; completion.Passed = false; await PersistAsync(actor, completion, true); await AuditAsync(actor, completion, AuditLogTypes.ChecklistCompletionStarted);
				return completion.Id;
			});
		}
		public async Task SkipOccurrenceAsync(ChecklistActor actor, string occurrenceId, int revision, string reason)
		{
			Id(occurrenceId); await RequireWriteAsync(actor, true);
			if (string.IsNullOrWhiteSpace(reason) || reason.Length > 4000) throw new ChecklistException(400, "SkipReasonRequired");
			await TransactionAsync(actor, async events =>
			{
				var row = await RevealAsync(actor, await _store.GetAsync<ChecklistOccurrence>(actor.DepartmentId, occurrenceId)); Revision(row, revision);
				if (row.ScheduleId == null || row.State != 3 && row.State != 4 || await _store.GetAsync<ChecklistCompletion>(actor.DepartmentId, row.CompletionId) != null) throw new ChecklistException(409, "OccurrenceUnavailable");
				await _authorization.TargetAsync(actor, (ChecklistTargetType)row.TargetType, row.TargetId);
				row.State = (int)ChecklistOccurrenceState.Skipped; row.Revision++; row.Content = JsonConvert.SerializeObject(new { Reason = reason.Trim() });
				await PersistAsync(actor, row, false); await AuditAsync(actor, row, AuditLogTypes.ChecklistOccurrenceSkipped);
				var schedule = await _store.GetAsync<ChecklistSchedule>(actor.DepartmentId, row.ScheduleId);
				await ScheduleEventAsync(schedule, row, WorkflowTriggerEventType.ChecklistOccurrenceSkipped, events, _clock.GetUtcNow().UtcDateTime); return true;
			});
		}
		private async Task ScheduleEventAsync(ChecklistSchedule schedule, ChecklistOccurrence occurrence, WorkflowTriggerEventType trigger, List<long> events, DateTime now)
		{
			var entry = await _outbox.EnqueueAsync(schedule.DepartmentId, "Checklists", new DomainEventEnvelope
			{
				EventName = trigger.ToString(), AggregateType = occurrence == null ? "ChecklistSchedule" : "ChecklistOccurrence", AggregateId = occurrence?.Id ?? schedule.Id,
				AggregateVersion = occurrence?.Revision ?? schedule.Revision, Trigger = trigger, OccurredOn = now,
				Payload = new { ScheduleId = schedule.Id, OccurrenceId = occurrence?.Id, CompletionId = occurrence?.CompletionId, DefinitionId = schedule.ParentId, VersionId = occurrence?.VersionId ?? schedule.VersionId,
					TargetType = occurrence?.TargetType ?? schedule.TargetType, TargetId = occurrence?.TargetId ?? schedule.TargetId, State = occurrence?.State, IsActive = schedule.IsActive, Revision = occurrence?.Revision ?? schedule.Revision,
					PeriodStartUtc = occurrence?.PeriodStartUtc, WindowEndUtc = occurrence?.WindowEndUtc }
			}); events.Add(entry.DomainEventOutboxId);
		}
		private async Task WorkerWriteAsync<T>(T row, bool insert, CancellationToken ct) where T : ChecklistRow
		{
			var result = await _write.Value.PrepareRecordsEntityWriteAsync(row.DepartmentId, row, (T)null, row.Id, ChecklistTables.Fields<T>(), () => row.IsProtected = true, null, null, true, ct);
			if (result?.Success != true || result.IsProtected && HasPlaintextFields(row)) throw new InvalidOperationException("Checklist scheduling requires an available ADP catalog and write broker.");
			await _store.WriteAsync(row, insert, ct);
			if (row is ChecklistOccurrence) _refreshRow = row;
		}
		private async Task WorkerAuditAsync(ChecklistRow row, AuditLogTypes type, DateTime now, CancellationToken ct)
		{
			var audit = await _audit.InsertAsync(new AuditLog { DepartmentId = row.DepartmentId, ObjectDepartmentId = row.DepartmentId, ObjectId = row.Id, UserId = "system", LogType = (int)type, Message = type.ToString(), LoggedOn = now, Successful = true, ServerName = Environment.MachineName }, ct);
			audit.Data = JsonConvert.SerializeObject(new { row.Id, row.Revision });
			var result = await _write.Value.PrepareRecordsEntityWriteAsync(row.DepartmentId, audit, null, audit.AuditLogId.ToString(CultureInfo.InvariantCulture), ReadinessHistoryFields.Audits, null, null, null, true, ct);
			if (result?.Success != true || result.IsProtected && !ProtectedDataEnvelope.HasEnvelopePrefix(audit.Data)) throw new InvalidOperationException("Checklist scheduling audit protection is unavailable.");
			await _audit.UpdateAsync(audit, ct);
		}
		public async Task<ChecklistScheduleSweepResult> SweepSchedulesAsync(DateTime utcNow, CancellationToken ct = default)
		{
			var result = new ChecklistScheduleSweepResult(); var now = ChecklistRecurrence.Utc(utcNow); var afterDepartment = 0;
			while (true)
			{
				var departments = await _store.SchedulingDepartmentsAsync(afterDepartment, ct); if (departments.Count == 0) break;
				foreach (var department in departments)
				{
					var afterSchedule = "";
					while (true)
					{
						var schedules = await _store.ActiveSchedulesAsync(department, afterSchedule, ct); if (schedules.Count == 0) break;
						foreach (var candidate in schedules)
						{
							ct.ThrowIfCancellationRequested(); var events = new List<long>(); var generated = 0; var missed = 0; _refreshRow = null;
							try
							{
								await _uow.CreateOrGetConnectionAsync(ct); await _store.LockDepartmentAsync(department, ct);
								var schedule = await _store.GetAsync<ChecklistSchedule>(department, candidate.Id, ct);
								if (!schedule.IsActive) { _uow.CommitChanges(); continue; }
								var enabled = await _access.CanUseChecklistsAsync(department) && (schedule.TargetType != (int)ChecklistTargetType.InventoryAsset || _assets != null && await _assets.IsAvailableAsync(department));
								if (!enabled)
								{
									schedule.IsSuspended = true; schedule.UpdatedOn = now; await WorkerWriteAsync(schedule, false, ct); _uow.CommitChanges(); continue;
								}
								var definition = await _store.GetAsync<ChecklistDefinition>(department, schedule.ParentId, ct);
								if (definition == null || definition.Retired || definition.DeletedOn.HasValue)
								{
									schedule.IsActive = false; schedule.Revision++; await _store.CancelUnstartedOccurrencesAsync(department, schedule.Id, null, now, ct);
									await WorkerWriteAsync(schedule, false, ct); await WorkerAuditAsync(schedule, AuditLogTypes.ChecklistScheduleUpdated, now, ct); await ScheduleEventAsync(schedule, null, WorkflowTriggerEventType.ChecklistScheduleChanged, events, now);
								}
								else
								{
									if (schedule.IsSuspended)
									{
										await _store.CancelUnstartedOccurrencesAsync(department, schedule.Id, null, now, ct); schedule.IsSuspended = false; schedule.Revision++; schedule.ActiveFromUtc = now; schedule.GeneratedThroughUtc = now; schedule.LastSweepUtc = now;
									}
									var from = ChecklistRecurrence.Utc(schedule.GeneratedThroughUtc);
									// Workshift days can be added/moved inside an already generated horizon. Reconcile their future periods each sweep.
									if (schedule.WorkshiftId != null) from = new[] { now, schedule.LastSweepUtc }.Min();
									var until = new[] { now.AddDays(7), from.AddDays(schedule.WorkshiftId == null ? 7 : 8) }.Min();
									var starts = schedule.WorkshiftId == null ? null : await _store.WorkshiftStartsAsync(department, schedule.WorkshiftId, from, until, ct);
									var periods = ChecklistRecurrence.Expand(schedule, from, until, starts);
									var existing = await _store.OccurrencesInWindowAsync(department, schedule.Id, from, until, ct);
									foreach (var removed in existing.Where(o => schedule.WorkshiftId != null && o.State == 3 && o.PeriodStartUtc >= now && !periods.Contains(o.PeriodStartUtc.Value)))
									{ removed.State = 6; removed.Revision++; removed.UpdatedOn = now; await WorkerWriteAsync(removed, false, ct); }
									foreach (var period in periods)
									{
										if (existing.Any(o => o.PeriodStartUtc == period && o.TargetType == schedule.TargetType && o.TargetId == schedule.TargetId && new[] { 0, 1, 2, 5 }.Contains(o.State))) continue;
										var prior = existing.FirstOrDefault(o => o.ScheduleRevision == schedule.Revision && o.PeriodStartUtc == period);
										if (prior != null)
										{
											if (prior.State == 6 && period >= now) { prior.State = 3; prior.Revision++; prior.UpdatedOn = now; await WorkerWriteAsync(prior, false, ct); generated++; }
											continue;
										}
										var occurrence = new ChecklistOccurrence { DepartmentId = department, ParentId = schedule.ParentId, VersionId = schedule.VersionId, CompletionId = Guid.NewGuid().ToString(), ScheduleId = schedule.Id, ScheduleRevision = schedule.Revision,
											TargetType = schedule.TargetType, TargetId = schedule.TargetId, State = (int)ChecklistOccurrenceState.Scheduled, PeriodStartUtc = period, WindowEndUtc = period.AddMinutes(schedule.WindowMinutes), CreatedBy = "system", CreatedOn = now, UpdatedOn = now };
										await WorkerWriteAsync(occurrence, true, ct); generated++;
									}
									if (until > schedule.GeneratedThroughUtc) schedule.GeneratedThroughUtc = until;
									schedule.LastSweepUtc = new[] { now, until }.Min();
									foreach (var occurrence in await _store.ScheduledOccurrencesAsync(department, schedule.Id, schedule.ActiveFromUtc, now, ct))
									{
										if (!occurrence.WindowEndUtc.HasValue || occurrence.WindowEndUtc >= now || occurrence.WindowEndUtc < schedule.ActiveFromUtc) continue;
										occurrence.State = (int)ChecklistOccurrenceState.Missed; occurrence.MissedOn = now; occurrence.Revision++; occurrence.UpdatedOn = now;
										await WorkerWriteAsync(occurrence, false, ct); await WorkerAuditAsync(occurrence, AuditLogTypes.ChecklistOccurrenceMissed, now, ct);
										await ScheduleEventAsync(schedule, occurrence, WorkflowTriggerEventType.ChecklistMissed, events, now); missed++;
									}
									await WorkerWriteAsync(schedule, false, ct);
								}
								await RefreshEventAsync(events);
								_uow.CommitChanges(); result.Generated += generated; result.Missed += missed;
								await _outbox.DispatchAfterCommitAsync(events, ct);
							}
							catch (OperationCanceledException) when (ct.IsCancellationRequested) { _uow.DiscardChanges(); throw; }
							catch (Exception ex) { _uow.DiscardChanges(); result.Errors++; Resgrid.Framework.Logging.LogError($"Checklist scheduling failed for department {department}, schedule {candidate.Id}: {ex.GetType().FullName}."); }
						}
						afterSchedule = schedules.Last().Id;
					}
				}
				afterDepartment = departments.Last();
			}
			return result;
		}
	}
}
