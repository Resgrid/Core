using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
    public sealed partial class WorkOrdersService
    {
        public static TimeZoneInfo MaintenanceZone(string id)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id ?? "UTC"); }
            catch (TimeZoneNotFoundException) { throw new WorkOrderException(400, "RecurrenceInvalid"); }
            catch (InvalidTimeZoneException) { throw new WorkOrderException(400, "RecurrenceInvalid"); }
        }
        public static DateTime MaintenanceUtc(DateTime local, string zoneId)
        {
            var zone = MaintenanceZone(zoneId); local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            // Gaps advance to the first valid minute; folds choose the later UTC occurrence once.
            for (var i = 0; zone.IsInvalidTime(local) && i < 180; i++) local = local.AddMinutes(1);
            if (zone.IsInvalidTime(local)) throw new WorkOrderException(400, "RecurrenceInvalid");
            if (zone.IsAmbiguousTime(local)) return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Min()).UtcDateTime;
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }
        private static DateTime CalendarAt(WorkOrderRecurrence row, int index) => (MaintenanceCalendar)row.Calendar switch
        {
            MaintenanceCalendar.Daily => row.AnchorLocal.AddDays((long)row.Interval * index),
            MaintenanceCalendar.Weekly => row.AnchorLocal.AddDays((long)row.Interval * index * 7),
            MaintenanceCalendar.Monthly => row.AnchorLocal.AddMonths(checked(row.Interval * index)),
            MaintenanceCalendar.Quarterly => row.AnchorLocal.AddMonths(checked(row.Interval * index * 3)),
            MaintenanceCalendar.Yearly => row.AnchorLocal.AddYears(checked(row.Interval * index)),
            _ => throw new WorkOrderException(400, "RecurrenceInvalid")
        };
        public static DateTime? NextMaintenanceDue(WorkOrderRecurrence row, DateTime? after)
        {
            if (row.Calendar == 0) return null;
            for (var cycle = 0; cycle < 80000; cycle++)
            {
                DateTime local;
                try { local = CalendarAt(row, cycle); } catch (ArgumentOutOfRangeException) { return null; }
                if (local.Year > 2200 || row.EndOn.HasValue && local.Date > row.EndOn.Value.Date) return null;
                var utc = MaintenanceUtc(local, row.TimeZoneId);
                if (!after.HasValue || utc > after) return utc;
            }
            throw new WorkOrderException(400, "RecurrenceInvalid");
        }
        public static DateTime ServiceDue(WorkOrderRecurrence row, DateTime originalUtc)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(originalUtc, DateTimeKind.Utc), MaintenanceZone(row.TimeZoneId));
            for (var i = 0; i < 732; i++)
            {
                if (row.BlackoutFrom.HasValue && local.Date >= row.BlackoutFrom.Value.Date && local.Date <= row.BlackoutUntil.Value.Date) { local = row.BlackoutUntil.Value.Date.AddDays(1); continue; }
                var minute = local.Hour * 60 + local.Minute;
                if ((row.ServiceWeekdays & (1 << (int)local.DayOfWeek)) == 0 || minute > row.ServiceEndMinute) { local = local.Date.AddDays(1); continue; }
                if (minute < row.ServiceStartMinute) local = local.Date.AddMinutes(row.ServiceStartMinute);
                return MaintenanceUtc(local, row.TimeZoneId);
            }
            throw new WorkOrderException(400, "RecurrenceInvalid");
        }
        private async Task RequireRecurrenceAsync(ChecklistActor actor, WorkOrderRecurrence row)
        {
            await _authorization.RequireMemberAsync(actor);
            if (row == null || row.DepartmentId != actor.DepartmentId || !await _authorization.CanManageAsync(actor, row.TargetGroupId)) throw new WorkOrderException(404, "Unavailable");
        }
        private async Task<WorkOrderRecurrenceView> RecurrenceViewAsync(ChecklistActor actor, WorkOrderRecurrence row, bool history, int historyPage = 0)
        {
            await RequireRecurrenceAsync(actor, row); await RevealAsync(actor, row);
            var version = await RevealAsync(actor, await _store.GetAsync<WorkOrderRecurrenceVersion>(actor.DepartmentId, row.CurrentVersionId));
            var settings = Decode<WorkOrderRecurrenceInput>(version.Content); settings.Id = row.Id; settings.Revision = row.Revision; settings.IsActive = row.IsActive; settings.MeterBaseline = row.MeterBaseline; settings.AnchorLocal = row.AnchorLocal;
            var view = new WorkOrderRecurrenceView { HistoryPage = historyPage, Schedule = row, Settings = settings };
            if (history)
            {
                foreach (var change in await _maintenance.QueryMaintenanceAsync<WorkOrderRecurrenceChange>(actor.DepartmentId, "RecurrenceId", row.Id, historyPage * 500)) view.Changes.Add(await RevealAsync(actor, change));
                foreach (var reading in await _maintenance.QueryMaintenanceAsync<WorkOrderMeterReading>(actor.DepartmentId, "RecurrenceId", row.Id, historyPage * 500)) view.Readings.Add(await RevealAsync(actor, reading));
            }
            view.HasMoreHistory = view.Changes.Count == 500 || view.Readings.Count == 500;
            return view;
        }
        public async Task<WorkOrderRecurrenceView> RecurrenceAsync(ChecklistActor actor, int id, int historyPage = 0)
        {
            RequireMaintenanceStore(); if (historyPage < 0 || historyPage > 10000) throw new WorkOrderException(400, "RecurrenceInvalid");
            return await RecurrenceViewAsync(actor, await _store.GetAsync<WorkOrderRecurrence>(actor.DepartmentId, id), true, historyPage);
        }
        public async Task<List<WorkOrderRecurrenceView>> RecurrencesAsync(ChecklistActor actor, int page = 0)
        {
            RequireMaintenanceStore(); await _authorization.RequireMemberAsync(actor);
            if (page < 0 || page > 10000) throw new WorkOrderException(400, "RecurrenceInvalid");
            var result = new List<WorkOrderRecurrenceView>(); var remaining = page * 50;
            for (var skip = 0; ; skip += 500)
            {
                var rows = await _maintenance.QueryMaintenanceAsync<WorkOrderRecurrence>(actor.DepartmentId, skip: skip);
                foreach (var row in rows)
                {
                    if (!await _authorization.CanManageAsync(actor, row.TargetGroupId)) continue;
                    if (remaining-- > 0) continue;
                    result.Add(await RecurrenceViewAsync(actor, row, false)); if (result.Count == 51) return result;
                }
                if (rows.Count < 500) return result;
            }
        }
        public async Task<int> SaveRecurrenceAsync(ChecklistActor actor, WorkOrderRecurrenceInput input)
        {
            RequireMaintenanceStore();
            if (input?.Template == null) throw new WorkOrderException(400, "RecurrenceInvalid");
            input.Template.Content ??= new WorkOrderContent();
            input.Template.Content.Resolution = input.Template.Content.Cause = input.Template.Content.VerificationEvidence = null;
            foreach (var step in input.Template.Content.Steps ?? new()) step.Completed = false;
            input.Template.DueOn = null;
            input.Template.Type = WorkOrderType.Preventive; Validate(input.Template);
            if (!Enum.IsDefined(input.Calendar) || !Enum.IsDefined(input.MeterUnit) || !Enum.IsDefined(input.Condition) || input.Interval < 1 || input.Interval > 120
                || input.Calendar == 0 && input.MeterUnit == 0 && input.Condition == 0 || input.AnchorLocal.Year < 2000 || input.AnchorLocal.Year > 2200
                || input.EndOn?.Date < input.AnchorLocal.Date || input.EndOn?.Year > 2200 || input.LeadDays < 0 || input.LeadDays > 90
                || input.ServiceWeekdays < 1 || input.ServiceWeekdays > 127 || input.ServiceStartMinute < 0 || input.ServiceEndMinute > 1439 || input.ServiceStartMinute > input.ServiceEndMinute
                || input.BlackoutFrom.HasValue != input.BlackoutUntil.HasValue || input.BlackoutUntil < input.BlackoutFrom || input.BlackoutUntil - input.BlackoutFrom > TimeSpan.FromDays(366)
                || input.MeterUnit != 0 && (input.MeterInterval is null or <= 0 || input.MeterInterval > 100000000 || input.MeterBaseline is null or < 0 or > 1000000000000m)
                || new[] { input.MeterInterval, input.MeterBaseline, input.ConditionThreshold }.Any(v => v.HasValue && decimal.Round(v.Value, 6) != v)
                || input.ConditionThreshold < -1000000000m || input.ConditionThreshold > 1000000000m
                || input.Condition != 0 && (!input.ConditionThreshold.HasValue || string.IsNullOrWhiteSpace(input.ConditionUnit))
                || input.EscalateAfterMinutes < 0 || input.EscalateAfterMinutes > 525600) throw new WorkOrderException(400, "RecurrenceInvalid");
            MaintenanceZone(input.TimeZoneId); Text(input.ConditionUnit, 100); Text(input.Reason, 4000, input.Id != 0);
            return await TransactionAsync(actor, async events =>
            {
                await _authorization.ValidateTargetAsync(actor, input.Template);
                if (!await _authorization.CanManageAsync(actor, input.Template.TargetGroupId)) throw new WorkOrderException(403, "PermissionRequired");
                if (input.Id == 0)
                {
                    var existing = (await _maintenance.QueryMaintenanceAsync<WorkOrderRecurrence>(actor.DepartmentId, "RequestId", input.Template.RequestId)).SingleOrDefault();
                    if (existing != null)
                    {
                        await RequireRecurrenceAsync(actor, existing);
                        var pinned = await RevealAsync(actor, await _store.GetAsync<WorkOrderRecurrenceVersion>(actor.DepartmentId, existing.CurrentVersionId));
                        if (existing.CreatedBy != actor.UserId || pinned.Content != JsonConvert.SerializeObject(input)) throw new WorkOrderException(409, "Conflict");
                        return existing.Id;
                    }
                }
                var row = input.Id == 0 ? New<WorkOrderRecurrence>(actor) : await _store.GetAsync<WorkOrderRecurrence>(actor.DepartmentId, input.Id);
                if (row == null) throw new WorkOrderException(404, "Unavailable");
                row.RequestId ??= input.Template.RequestId;
                var calendarChanged = row.Calendar != (int)input.Calendar || row.Interval != input.Interval || row.TimeZoneId != input.TimeZoneId || row.AnchorLocal != input.AnchorLocal;
                if (input.Id != 0)
                {
                    await RequireRecurrenceAsync(actor, row); await RevealAsync(actor, row); Revision(row, input.Revision);
                    if (row.TargetUnitId != input.Template.TargetUnitId || row.InventoryAssetId != input.Template.InventoryAssetId) throw new WorkOrderException(409, "TargetUnavailable");
                    if (row.PendingWorkOrderId.HasValue && (row.TargetUnitId != input.Template.TargetUnitId || row.InventoryAssetId != input.Template.InventoryAssetId || row.MeterUnit != (int)input.MeterUnit || row.MeterBaseline != input.MeterBaseline)) throw new WorkOrderException(409, "RecurrencePending");
                    row.Revision++;
                }
                var assignment = new WorkOrder { DepartmentId = actor.DepartmentId, TargetGroupId = input.Template.TargetGroupId };
                if (input.AssignedToUserId != null || input.AssignedToRoleId.HasValue) await _authorization.ValidateAssignmentAsync(actor, assignment, input.AssignedToUserId, input.AssignedToRoleId);
                if (input.EscalationRoleId.HasValue) await _authorization.ValidateAssignmentAsync(actor, assignment, null, input.EscalationRoleId);
                row.TargetUnitId = input.Template.TargetUnitId; row.TargetGroupId = input.Template.TargetGroupId; row.InventoryAssetId = input.Template.InventoryAssetId;
                row.AssignedToUserId = input.AssignedToUserId; row.AssignedToRoleId = input.AssignedToRoleId; row.Priority = (int)input.Template.Priority;
                row.IsActive = input.IsActive; row.Calendar = (int)input.Calendar; row.Interval = input.Interval; row.TimeZoneId = input.TimeZoneId;
                row.AnchorLocal = DateTime.SpecifyKind(input.AnchorLocal, DateTimeKind.Unspecified); row.EndOn = input.EndOn?.Date; row.LeadDays = input.LeadDays; row.CompletionBased = input.CompletionBased;
                row.ServiceWeekdays = input.ServiceWeekdays; row.ServiceStartMinute = input.ServiceStartMinute; row.ServiceEndMinute = input.ServiceEndMinute;
                row.BlackoutFrom = input.BlackoutFrom?.Date; row.BlackoutUntil = input.BlackoutUntil?.Date;
                if (input.Id != 0 && (row.MeterUnit != (int)input.MeterUnit || row.MeterBaseline != input.MeterBaseline)) throw new WorkOrderException(409, "UseMeterReset");
                row.MeterUnit = (int)input.MeterUnit; row.MeterInterval = input.MeterInterval; row.MeterBaseline = input.MeterBaseline; row.LastMeterValue ??= input.MeterBaseline;
                row.Condition = (int)input.Condition; row.ConditionThreshold = input.ConditionThreshold;
                row.EscalateAfterMinutes = input.EscalateAfterMinutes; row.EscalationRoleId = input.EscalationRoleId;
                if (input.Id == 0 || calendarChanged) row.NextDueOn = NextMaintenanceDue(row, input.Id == 0 ? null : Now);
                // Meter provenance changes only through a separately audited reset command.
                row.Content = JsonConvert.SerializeObject(new { Name = input.Template.Content.Title });
                if (input.Id == 0) await SaveAsync(actor, row, true);
                var version = New<WorkOrderRecurrenceVersion>(actor); version.RecurrenceId = row.Id; version.Content = JsonConvert.SerializeObject(input); await SaveAsync(actor, version, true);
                row.CurrentVersionId = version.Id; await SaveAsync(actor, row);
                await RecurrenceChangeAsync(actor, row, MaintenanceChangeType.Configured, input.Reason, null, null);
                await RecurrenceEventAsync(row, WorkflowTriggerEventType.WorkOrderRecurrenceChanged, events); return row.Id;
            });
        }
        private async Task RecurrenceChangeAsync(ChecklistActor actor, WorkOrderRecurrence row, MaintenanceChangeType type, string note, DateTime? original, DateTime? revised)
        {
            var change = New<WorkOrderRecurrenceChange>(actor); change.RecurrenceId = row.Id; change.ChangeType = (int)type; change.OriginalDueOn = original; change.RevisedDueOn = revised;
            change.Content = JsonConvert.SerializeObject(new { Note = note, row.CurrentVersionId }); await SaveAsync(actor, change, true);
        }
        private async Task RecurrenceEventAsync(WorkOrderRecurrence row, WorkflowTriggerEventType trigger, List<long> events)
        {
            var entry = await _outbox.EnqueueAsync(row.DepartmentId, "WorkOrders", new DomainEventEnvelope { EventName = trigger.ToString(), AggregateType = "WorkOrderRecurrence", AggregateId = "recurrence:" + row.Id, AggregateVersion = row.Revision, Trigger = trigger, OccurredOn = Now,
                Payload = new { RecurrenceId = row.Id, row.Revision, row.TargetUnitId, row.TargetGroupId, row.InventoryAssetId, DueOn = row.NextDueOn } }); events.Add(entry.DomainEventOutboxId);
        }
        public async Task RecordReadingAsync(ChecklistActor actor, int id, WorkOrderReadingInput input)
        {
            RequireMaintenanceStore();
            if (input == null || !Guid.TryParseExact(input.RequestId, "D", out _) || input.ObservedOn.Year < 2000 || input.ObservedOn > Now.AddMinutes(5)
                || input.MeterValue < 0 || input.MeterValue > 1000000000000m || input.ConditionValue < -1000000000m || input.ConditionValue > 1000000000m
                || new[] { input.MeterValue, input.ConditionValue }.Any(v => v.HasValue && decimal.Round(v.Value, 6) != v)
                || !input.MeterValue.HasValue && !input.ConditionValue.HasValue) throw new WorkOrderException(400, "ReadingInvalid");
            input.ObservedOn = DateTime.SpecifyKind(input.ObservedOn, DateTimeKind.Utc); Text(input.Source, 1000, true); Text(input.Note, 4000, input.ResetMeter);
            await TransactionAsync(actor, async events =>
            {
                var row = await _store.GetAsync<WorkOrderRecurrence>(actor.DepartmentId, id); await RequireRecurrenceAsync(actor, row); await RevealAsync(actor, row);
                var existing = (await _maintenance.QueryMaintenanceAsync<WorkOrderMeterReading>(actor.DepartmentId, "RequestId", input.RequestId)).SingleOrDefault();
                if (existing != null)
                {
                    await RevealAsync(actor, existing);
                    if (existing.RecurrenceId != id || existing.CreatedBy != actor.UserId || existing.Content != JsonConvert.SerializeObject(input)) throw new WorkOrderException(409, "Conflict");
                    return true;
                }
                Revision(row, input.Revision);
                if (row.LastReadingOn.HasValue && input.ObservedOn <= row.LastReadingOn || input.MeterValue.HasValue && row.MeterUnit == 0 || input.ConditionValue.HasValue && row.Condition == 0
                    || input.ResetMeter && (!input.MeterValue.HasValue || row.PendingWorkOrderId.HasValue) || !input.ResetMeter && input.MeterValue < row.LastMeterValue) throw new WorkOrderException(409, "ReadingInvalid");
                if (input.ResetMeter) { row.MeterEpoch++; row.MeterBaseline = input.MeterValue; row.ReadingDue = false; row.ReadingDueOn = null; }
                var reading = New<WorkOrderMeterReading>(actor); reading.RecurrenceId = id; reading.RequestId = input.RequestId; reading.MeterEpoch = row.MeterEpoch; reading.ObservedOn = input.ObservedOn;
                reading.Content = JsonConvert.SerializeObject(input); await SaveAsync(actor, reading, true);
                var before = row.ReadingDue; row.LastReadingOn = input.ObservedOn; if (input.MeterValue.HasValue) row.LastMeterValue = input.MeterValue;
                if (row.MeterInterval.HasValue && input.MeterValue >= row.MeterBaseline + row.MeterInterval) row.ReadingDue = true;
                if (input.ConditionValue.HasValue)
                {
                    var reached = row.Condition == (int)MaintenanceCondition.AtOrAbove ? input.ConditionValue >= row.ConditionThreshold : input.ConditionValue <= row.ConditionThreshold;
                    if (reached && !row.ConditionLatched) row.ReadingDue = true;
                    row.ConditionLatched = reached;
                }
                if (!before && row.ReadingDue) row.ReadingDueOn = input.ObservedOn;
                row.Revision++; await SaveAsync(actor, row); await RecurrenceChangeAsync(actor, row, input.ResetMeter ? MaintenanceChangeType.MeterReset : MaintenanceChangeType.Reading, input.Note, null, null);
                if (!before && row.ReadingDue) await RecurrenceEventAsync(row, WorkflowTriggerEventType.WorkOrderThresholdReached, events); return true;
            });
        }
        public async Task DeferAsync(ChecklistActor actor, int orderId, WorkOrderDeferralInput input)
        {
            RequireMaintenanceStore();
            if (input == null || input.DueOn <= Now || input.DueOn > Now.AddDays(366)) throw new WorkOrderException(400, "RecurrenceInvalid");
            Text(input.Reason, 4000, true); input.DueOn = DateTime.SpecifyKind(input.DueOn, DateTimeKind.Utc);
            await TransactionAsync(actor, async events =>
            {
                var order = await ReadOrderAsync(actor, orderId); Revision(order, input.Revision);
                if (Terminal(order) || order.Status == 5 || !await _authorization.CanManageAsync(actor, order.TargetGroupId)) throw new WorkOrderException(403, "PermissionRequired");
                order.OriginalDueOn ??= order.DueOn; var original = order.DueOn; order.DueOn = input.DueOn; order.EscalatedOn = null;
                await ChangedAsync(actor, order, WorkOrderActivityType.Deferred, events, input.Reason, trigger: WorkflowTriggerEventType.WorkOrderDeferred, previousDue: original);
                if (int.TryParse(order.WorkOrderRecurrenceId, out var scheduleId))
                {
                    var recurrence = await _store.GetAsync<WorkOrderRecurrence>(actor.DepartmentId, scheduleId);
                    await RecurrenceChangeAsync(actor, recurrence, MaintenanceChangeType.Deferred, input.Reason, original, input.DueOn);
                }
                return true;
            });
        }
        private async Task RecurrenceReopenedAsync(ChecklistActor actor, WorkOrder order, string reason, List<long> events)
        {
            if (!int.TryParse(order.WorkOrderRecurrenceId, out var id) || _maintenance == null) return;
            Text(reason, 4000, true);
            var row = await _store.GetAsync<WorkOrderRecurrence>(actor.DepartmentId, id);
            if (row == null || row.PendingWorkOrderId.HasValue && row.PendingWorkOrderId != order.Id || row.Cycle != order.RecurrenceCycle) throw new WorkOrderException(409, "RecurrencePending");
            row.IsActive = false; row.PendingWorkOrderId = order.Id; row.UpdatedOn = Now; row.Revision++; await _store.WriteAsync(row);
            await RecurrenceChangeAsync(actor, row, MaintenanceChangeType.Configured, reason, order.OriginalDueOn, row.NextDueOn);
            await RecurrenceEventAsync(row, WorkflowTriggerEventType.WorkOrderRecurrenceChanged, events);
        }
        private async Task RecurrenceCompletedAsync(WorkOrder order)
        {
            if (!Terminal(order) || !int.TryParse(order.WorkOrderRecurrenceId, out var id) || _maintenance == null) return;
            var row = await _store.GetAsync<WorkOrderRecurrence>(order.DepartmentId, id);
            if (row?.PendingWorkOrderId != order.Id) return;
            if (order.Status != (int)WorkOrderStatus.Closed)
            {
                // An uncompleted cycle stays due. Pause for an attended rescheduling decision.
                row.IsActive = false; row.PendingWorkOrderId = null; row.UpdatedOn = Now; row.Revision++; await _store.WriteAsync(row); return;
            }
            if (row.CompletionBased) row.AnchorLocal = TimeZoneInfo.ConvertTimeFromUtc(Now, MaintenanceZone(row.TimeZoneId));
            row.NextDueOn = NextMaintenanceDue(row, row.CompletionBased ? Now : order.OriginalDueOn ?? order.DueOn);
            row.PendingWorkOrderId = null; row.MeterBaseline = row.LastMeterValue ?? row.MeterBaseline; row.ReadingDue = false; row.ReadingDueOn = null;
            row.UpdatedOn = Now; row.Revision++; await _store.WriteAsync(row); // Preserve ciphertext; only reviewed cursor metadata changes.
        }
        public async Task<WorkOrderMaintenanceSweep> GenerateMaintenanceAsync(int departmentId)
        {
            RequireMaintenanceStore(); var result = new WorkOrderMaintenanceSweep();
            if (!await _access.CanUseMaintenanceAsync(departmentId)) return result;
            for (var skip = 0; ; skip += 500)
            {
                var intents = await _maintenance.QueryMaintenanceAsync<WorkOrderFailureIntent>(departmentId, skip: skip);
                foreach (var intent in intents.Where(i => !i.ProcessedOn.HasValue))
                    try { await ProcessFailureAsync(departmentId, intent.Id, result); } catch { result.Errors++; }
                if (intents.Count < 500) break;
            }
            for (var skip = 0; ; skip += 500)
            {
                var rows = await _maintenance.QueryMaintenanceAsync<WorkOrderRecurrence>(departmentId, skip: skip, pendingOnly: true);
                foreach (var candidate in rows)
                {
                    try
                    {
                        await WorkerTransactionAsync(departmentId, async events =>
                        {
                            var row = await _store.GetAsync<WorkOrderRecurrence>(departmentId, candidate.Id);
                            if (row?.IsActive != true || row.PendingWorkOrderId.HasValue || !row.ReadingDue && (!row.NextDueOn.HasValue || ServiceDue(row, row.NextDueOn.Value).AddDays(-row.LeadDays) > Now)) return;
                            if (row.EndOn.HasValue && TimeZoneInfo.ConvertTimeFromUtc(Now, MaintenanceZone(row.TimeZoneId)).Date > row.EndOn.Value.Date) return;
                            var version = await _store.GetAsync<WorkOrderRecurrenceVersion>(departmentId, row.CurrentVersionId);
                            if (version?.RecurrenceId != row.Id) throw new WorkOrderException(409, "RecurrenceInvalid");
                            var owner = new ChecklistActor { DepartmentId = departmentId, UserId = version.CreatedBy };
                            var target = await AutomatedTargetAsync(owner, row.TargetUnitId, row.TargetGroupId, row.InventoryAssetId);
                            if (target.TargetGroupId != row.TargetGroupId || target.TargetUnitId != row.TargetUnitId) throw new WorkOrderException(409, "TargetUnavailable");
                            var readingDue = row.ReadingDueOn ?? row.LastReadingOn ?? Now;
                            var original = row.ReadingDue && (!row.NextDueOn.HasValue || row.NextDueOn > readingDue) ? readingDue : row.NextDueOn.Value;
                            var due = ServiceDue(row, original);
                            if (ServiceDue(row, Now) > Now.AddMinutes(1)) return;
                            var request = MaintenanceIdentity("recurrence:" + departmentId + ":" + row.Id + ":" + (row.Cycle + 1));
                            var order = await _store.RequestAsync(departmentId, request);
                            if (order == null)
                            {
                                order = New<WorkOrder>(owner); order.RequestId = request; order.NumberYear = Now.Year; order.NumberSequence = await _store.NextNumberAsync(departmentId, order.NumberYear);
                                order.SourceType = 2; order.Type = (int)WorkOrderType.Preventive; order.Priority = row.Priority; order.TargetUnitId = row.TargetUnitId; order.TargetGroupId = row.TargetGroupId; order.InventoryAssetId = row.InventoryAssetId;
                                order.WorkOrderRecurrenceId = row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture); order.RecurrenceVersionId = row.CurrentVersionId; order.RecurrenceCycle = row.Cycle + 1;
                                order.DueOn = due; order.OriginalDueOn = original; order.EscalateAfterMinutes = row.EscalateAfterMinutes; order.EscalationRoleId = row.EscalationRoleId;
                                order.AssignedToUserId = row.AssignedToUserId; order.AssignedToRoleId = row.AssignedToRoleId;
                                order.Status = order.AssignedToUserId != null || order.AssignedToRoleId.HasValue ? (int)WorkOrderStatus.Assigned : (int)WorkOrderStatus.Accepted;
                                if (order.Status == 2) { await _authorization.ValidateAssignmentAsync(owner, order, order.AssignedToUserId, order.AssignedToRoleId); order.AssignedOn = Now; }
                                await PinSlaAsync(order); order.ResponseOn = Now;
                                await _store.AllocateAsync(order); await RecordGeneratedCreationAsync(order);
                                var generated = New<WorkOrderRecurrenceChange>(owner, order.Id); generated.RecurrenceId = row.Id; generated.ChangeType = (int)MaintenanceChangeType.Generated; generated.OriginalDueOn = original; generated.RevisedDueOn = due; await _store.AllocateAsync(generated);
                                await EventAsync(order, WorkflowTriggerEventType.WorkOrderCreated, events);
                                if (order.Status == 2) await EventAsync(order, WorkflowTriggerEventType.WorkOrderAssigned, events);
                                result.Generated++;
                            }
                            row.Cycle++; row.PendingWorkOrderId = order.Id; row.ReadingDue = false; row.ReadingDueOn = null; row.UpdatedOn = Now; row.Revision++; await _store.WriteAsync(row);
                        });
                    }
                    catch { result.Errors++; }
                }
                if (rows.Count < 500) break;
            }
            return result;
        }
        public async Task<WorkOrderMaintenanceSweep> EscalateMaintenanceAsync(int departmentId)
        {
            RequireMaintenanceStore(); var result = new WorkOrderMaintenanceSweep();
            if (!await _access.CanUseMaintenanceAsync(departmentId)) return result;
            await EscalateServiceLevelsAsync(departmentId, result);
            foreach (var candidate in await _maintenance.OverdueAsync(departmentId, Now))
            {
                try
                {
                    await WorkerTransactionAsync(departmentId, async events =>
                    {
                        var order = await _store.GetAsync<WorkOrder>(departmentId, candidate.Id);
                        if (order == null || order.Status >= 5 || order.EscalatedOn.HasValue || !order.DueOn.HasValue || order.DueOn.Value.AddMinutes(order.EscalateAfterMinutes) >= Now) return;
                        order.EscalatedOn = Now; order.UpdatedOn = Now; order.Revision++; await _store.WriteAsync(order);
                        await EventAsync(order, WorkflowTriggerEventType.WorkOrderOverdue, events); result.Escalated++;
                    });
                }
                catch { result.Errors++; }
            }
            return result;
        }
    }
}
