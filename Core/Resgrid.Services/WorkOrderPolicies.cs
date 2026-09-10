using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Resgrid.Model.Security;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
    public sealed partial class WorkOrdersService
    {
        private async Task<WorkOrderPolicy> PolicyRowAsync(int departmentId) => _maintenance == null ? null : (await _maintenance.QueryMaintenanceAsync<WorkOrderPolicy>(departmentId)).SingleOrDefault();
        private async Task<WorkOrderPolicyInput> ReadPolicyAsync(ChecklistActor actor)
        {
            var row = await PolicyRowAsync(actor.DepartmentId);
            if (row == null) return new WorkOrderPolicyInput();
            await RevealAsync(actor, row);
            var value = Decode<WorkOrderPolicyInput>(row.Content); value.Revision = row.Revision; value.Calendar = Decode<WorkOrderBusinessCalendar>(row.CalendarJson); return value;
        }
        private async Task RequirePolicyManagerAsync(ChecklistActor actor)
        {
            await _authorization.RequireMemberAsync(actor);
            if (!(await _authorization.ScopeAsync(actor)).All || !await _authorization.CanManageAsync(actor, null)) throw new WorkOrderException(403, "PermissionRequired");
        }
        public async Task<WorkOrderPolicyInput> PolicyAsync(ChecklistActor actor) { await RequirePolicyManagerAsync(actor); return await ReadPolicyAsync(actor); }
        public static void ValidateBusinessCalendar(WorkOrderBusinessCalendar c)
        {
            if (c == null || c.Weekdays is < 1 or > 127 || c.StartMinute is < 0 or > 1439 || c.EndMinute <= c.StartMinute || c.EndMinute > 1440 || c.Holidays == null || c.Holidays.Count > 366 || c.Targets == null || c.Targets.Count > 4) throw new WorkOrderException(400, "OperationsPolicyInvalid");
            MaintenanceZone(c.TimeZoneId);
            if (c.Holidays.Any(d => !DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) || c.Holidays.Distinct().Count() != c.Holidays.Count
                || c.Targets.Any(t => t == null || !Enum.IsDefined(t.Priority) || t.ResponseMinutes is < 1 or > 525600 || t.RepairMinutes < t.ResponseMinutes || t.RepairMinutes > 525600) || c.Targets.Select(t => t.Priority).Distinct().Count() != c.Targets.Count) throw new WorkOrderException(400, "OperationsPolicyInvalid");
        }
        public static DateTime AddBusinessMinutes(DateTime utc, int minutes, WorkOrderBusinessCalendar calendar)
        {
            ValidateBusinessCalendar(calendar);
            if (minutes is < 1 or > 525600) throw new WorkOrderException(400, "OperationsPolicyInvalid");
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc); var zone = MaintenanceZone(calendar.TimeZoneId);
            var day = TimeZoneInfo.ConvertTimeFromUtc(utc, zone).Date;
            var remaining = TimeSpan.FromMinutes(minutes);
            // Integrate actual UTC time inside configured local windows, including DST transitions.
            for (var i = 0; i < 3660; i++, day = day.AddDays(1))
            {
                if ((calendar.Weekdays & 1 << (int)day.DayOfWeek) == 0 || calendar.Holidays.Contains(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))) continue;
                var start = MaintenanceUtc(day.AddMinutes(calendar.StartMinute), calendar.TimeZoneId);
                var end = MaintenanceUtc(day.AddMinutes(calendar.EndMinute), calendar.TimeZoneId);
                if (start < utc) start = utc;
                if (end <= start) continue;
                if (end - start >= remaining) return start + remaining;
                remaining -= end - start;
            }
            throw new WorkOrderException(400, "OperationsPolicyInvalid");
        }
        public async Task SavePolicyAsync(ChecklistActor actor, WorkOrderPolicyInput input)
        {
            if (input == null || input.SpendingRules == null || input.SpendingRules.Count > 30 || input.SpendingRules.Any(r => r == null || !ValidCurrency(r.Currency) || r.Threshold < 0 || decimal.Round(r.Threshold, 2) != r.Threshold || r.Threshold > 100000000m)
                || input.SpendingRules.Select(r => r.Currency).Distinct().Count() != input.SpendingRules.Count || input.ApprovalsEnabled && input.SpendingRules.Count == 0) throw new WorkOrderException(400, "OperationsPolicyInvalid");
            ValidateBusinessCalendar(input.Calendar); await RequirePolicyManagerAsync(actor); RequireMaintenanceStore();
            await TransactionAsync(actor, async events =>
            {
                await RequirePolicyManagerAsync(actor); var row = await PolicyRowAsync(actor.DepartmentId); var previous = row == null ? null : (await RevealAsync(actor, row)).Content;
                if (row == null && input.Revision != 0 || row != null && row.Revision != input.Revision) throw new WorkOrderException(409, "Conflict");
                var insert = row == null; row ??= New<WorkOrderPolicy>(actor); if (!insert) row.Revision++;
                row.CalendarJson = JsonConvert.SerializeObject(input.Calendar); row.Content = JsonConvert.SerializeObject(input); await SaveAsync(actor, row, insert);
                var receipt = New<WorkOrderOperationReceipt>(actor); receipt.Kind = 2; receipt.RequestId = Guid.NewGuid().ToString("D"); receipt.Content = JsonConvert.SerializeObject(new { PolicyId = row.Id, row.Revision, Previous = previous, Settings = input }); await SaveAsync(actor, receipt, true);
                var audit = await _audit.InsertAsync(new AuditLog { DepartmentId = actor.DepartmentId, ObjectDepartmentId = actor.DepartmentId, UserId = actor.UserId, ObjectId = "WorkOrderPolicy:" + row.Id,
                    LogType = (int)AuditLogTypes.WorkOrderChanged, LoggedOn = Now, Successful = true, Message = "WorkOrderPolicyChanged", ServerName = Environment.MachineName }, CancellationToken.None);
                audit.Data = JsonConvert.SerializeObject(new { PolicyId = row.Id, row.Revision, ReceiptId = receipt.Id });
                var protection = await _write.Value.PrepareRecordsEntityWriteAsync(actor.DepartmentId, audit, null, audit.AuditLogId.ToString(CultureInfo.InvariantCulture), ReadinessHistoryFields.Audits, null, actor.GrantToken, actor.UserId, false);
                if (protection?.Success != true || protection.IsProtected && !ProtectedDataEnvelope.HasEnvelopePrefix(audit.Data)) throw new WorkOrderException(403, "ProtectedDataRequired");
                await _audit.UpdateAsync(audit, CancellationToken.None);
                var entry = await _outbox.EnqueueAsync(actor.DepartmentId, "WorkOrders", new DomainEventEnvelope { EventName = "WorkOrderPolicyChanged", AggregateType = "WorkOrderPolicy", AggregateId = Key(row), AggregateVersion = row.Revision, Trigger = WorkflowTriggerEventType.WorkOrderPolicyChanged, OccurredOn = Now, Payload = new { PolicyId = row.Id, row.Revision } }); events.Add(entry.DomainEventOutboxId); return true;
            });
        }
        private async Task PinSlaAsync(WorkOrder row)
        {
            row.ResponseDueOn = null; row.RepairDueOn = null; row.ResponseBreachedOn = null; row.RepairBreachedOn = null; row.SlaPolicyRevision = null;
            var policy = await PolicyRowAsync(row.DepartmentId); if (policy == null) return;
            var calendar = Decode<WorkOrderBusinessCalendar>(policy.CalendarJson); ValidateBusinessCalendar(calendar);
            var target = calendar.Targets.SingleOrDefault(t => (int)t.Priority == row.Priority); if (target == null) return;
            row.SlaPolicyRevision = policy.Revision;
            row.ResponseDueOn = AddBusinessMinutes(row.CreatedOn, target.ResponseMinutes, calendar);
            row.RepairDueOn = AddBusinessMinutes(row.CreatedOn, target.RepairMinutes, calendar);
        }
        private static bool ValidCurrency(string value) => value?.Length == 3 && value.All(c => c is >= 'A' and <= 'Z');
        private async Task RequireSpendingAsync(ChecklistActor actor, WorkOrder order, decimal? additional = 0m, string currency = null)
        {
            var policy = await ReadPolicyAsync(actor); if (!policy.ApprovalsEnabled) return;
            var content = Decode<StoredContent>(order.Content).Fields; currency ??= content.Currency;
            var rule = policy.SpendingRules.SingleOrDefault(r => r.Currency == currency);
            if (rule == null || currency != content.Currency || !additional.HasValue) throw new WorkOrderException(409, "SpendingApprovalRequired");
            decimal total = additional.Value;
            void Include(decimal? amount, string code) { if (!amount.HasValue || code != currency) throw new WorkOrderException(409, "SpendingApprovalRequired"); total += amount.Value; }
            foreach (var labor in await ChildrenAsync<WorkOrderLabor>(actor, order.Id)) { var c = Decode<WorkOrderLaborContent>(labor.Content); Include(c.RatePerHour.HasValue ? decimal.Round(c.Hours * c.RatePerHour.Value, 2, MidpointRounding.AwayFromZero) : null, c.Currency); }
            foreach (var part in (await ChildrenAsync<WorkOrderPart>(actor, order.Id)).Where(p => !p.VoidedOn.HasValue))
            {
                var c = Decode<WorkOrderPartContent>(part.Content); var quantity = part.Staged ? part.ReservedQuantity + part.IssuedQuantity : c.Quantity;
                if (quantity > 0) Include(c.UnitCost.HasValue ? decimal.Round(quantity * c.UnitCost.Value, 2, MidpointRounding.AwayFromZero) : null, c.Currency);
                if (part.Staged && part.ConsumedQuantity > 0) Include(c.ConsumedCost, c.Currency);
            }
            foreach (var charge in (await ChildrenAsync<WorkOrderVendorCharge>(actor, order.Id)).Where(c => !c.VoidedOn.HasValue)) { var c = Decode<WorkOrderVendorChargeContent>(charge.Content); Include(c.Amount, c.Currency); }
            total = Math.Max(total, content.EstimatedCost ?? 0);
            if (total <= rule.Threshold) return;
            if (content.Approval?.State != WorkOrderApprovalState.Approved || content.Approval.Currency != currency || content.Approval.Amount < total) throw new WorkOrderException(409, "SpendingApprovalRequired");
        }
        public async Task RequestApprovalAsync(ChecklistActor actor, int id, WorkOrderApprovalInput input)
        {
            if (input == null) throw new WorkOrderException(400, "InvalidInput"); Money(input.Amount); Text(input.Reason, 4000, true);
            await TransactionAsync(actor, async events =>
            {
                var row = await ReadOrderAsync(actor, id); Revision(row, input.Revision);
                if (Terminal(row) || row.Status == 5 || row.CreatedBy != actor.UserId && !await _authorization.CanContributeAsync(actor, row)) throw new WorkOrderException(403, "PermissionRequired");
                var document = Decode<StoredContent>(row.Content);
                document.Fields.Approval = new WorkOrderApproval { State = WorkOrderApprovalState.Requested, Amount = input.Amount, Currency = document.Fields.Currency, RequestedBy = actor.UserId, RequestedOn = Now, Reason = input.Reason };
                document.Fields.ApprovedCost = null; row.Content = JsonConvert.SerializeObject(document);
                await ChangedAsync(actor, row, WorkOrderActivityType.Updated, events, trigger: WorkflowTriggerEventType.WorkOrderApprovalChanged); return true;
            });
        }
        public async Task DecideApprovalAsync(ChecklistActor actor, int id, WorkOrderApprovalInput input)
        {
            if (input == null) throw new WorkOrderException(400, "InvalidInput"); Text(input.Reason, 4000, true);
            await TransactionAsync(actor, async events =>
            {
                var row = await ReadOrderAsync(actor, id); Revision(row, input.Revision); var document = Decode<StoredContent>(row.Content); var approval = document.Fields.Approval;
                if (Terminal(row) || row.Status == 5 || !await _authorization.CanManageAsync(actor, row.TargetGroupId) || approval == null || approval.RequestedBy == actor.UserId) throw new WorkOrderException(403, "IndependentApprovalRequired");
                if (approval.State != WorkOrderApprovalState.Requested || approval.Currency != document.Fields.Currency) throw new WorkOrderException(409, "Conflict");
                approval.State = input.Approve ? WorkOrderApprovalState.Approved : WorkOrderApprovalState.Rejected; approval.DecidedBy = actor.UserId; approval.DecidedOn = Now; approval.Reason = input.Reason;
                document.Fields.ApprovedCost = input.Approve ? approval.Amount : null; row.Content = JsonConvert.SerializeObject(document);
                await ChangedAsync(actor, row, WorkOrderActivityType.Updated, events, trigger: WorkflowTriggerEventType.WorkOrderApprovalChanged); return true;
            });
        }
    }
}
