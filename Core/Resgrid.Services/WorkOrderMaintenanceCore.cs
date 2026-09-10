using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
    public sealed partial class WorkOrdersService
    {
        private static string MaintenanceText(string key) => new ResourceManager("Resgrid.Localization.Areas.User.WorkOrders.WorkOrders", typeof(Resgrid.Localization.SupportedLocales).Assembly).GetString(key, CultureInfo.CurrentUICulture) ?? key;
        private void RequireMaintenanceStore() { if (_maintenance == null) throw new WorkOrderException(503, "MaintenanceUnavailable"); }
        private static InventoryActor InventoryActor(ChecklistActor actor) => new() { DepartmentId = actor.DepartmentId, UserId = actor.UserId, GrantToken = actor.GrantToken };
        private static string MaintenanceIdentity(string source) => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(source)).Take(16).ToArray()).ToString("D");
        private async Task<WorkOrder> RevealOrderAsync(ChecklistActor actor, WorkOrder row)
        {
            await RevealAsync(actor, row);
            if (!string.IsNullOrEmpty(row.Content)) return row;
            WorkOrderContent fields;
            if (row.SourceType == 2 && row.RecurrenceVersionId.HasValue)
            {
                var version = await RevealAsync(actor, await _store.GetAsync<WorkOrderRecurrenceVersion>(actor.DepartmentId, row.RecurrenceVersionId.Value));
                var settings = Decode<WorkOrderRecurrenceInput>(version.Content);
                fields = settings?.Template?.Content ?? throw new WorkOrderException(409, "MaintenanceUnavailable");
            }
            else if (row.SourceType == 1 && row.SourceChecklistCompletionId != null) fields = new WorkOrderContent { Title = MaintenanceText("GeneratedFailureTitle"), SafetyCritical = true };
            else throw new WorkOrderException(409, "MaintenanceUnavailable");
            // An attended projection only. First mutation seals a row-specific copy with its numeric AAD.
            row.Content = JsonConvert.SerializeObject(new StoredContent { Fields = fields }); return row;
        }
        public async Task ValidateFailureOptionsAsync(ChecklistActor actor, ChecklistForm form)
        {
            var items = form.Sections.SelectMany(s => s.Items).ToList();
            if (items.Any(i => !Enum.IsDefined(i.WorkOrderPriority) || !i.CreateWorkOrderOnFail && (i.SetUnitStateOnFail || i.HoldAssetOnFail))) throw new WorkOrderException(400, "InvalidInput");
            if (!items.Any(i => i.CreateWorkOrderOnFail)) return;
            await _authorization.RequireMemberAsync(actor);
            if (!await _authorization.CanManageAsync(actor, null)) throw new WorkOrderException(403, "PermissionRequired");
            if (items.Any(i => i.SetUnitStateOnFail) && form.TargetType is not (ChecklistTargetType.Unit or ChecklistTargetType.InventoryAsset)
                || items.Any(i => i.HoldAssetOnFail) && form.TargetType != ChecklistTargetType.InventoryAsset) throw new WorkOrderException(400, "TargetUnavailable");
            // Configuring the failure routing does not call billing from free Checklists.
        }
        public async Task RecordFailureIntentAsync(ChecklistActor actor, ChecklistCompletion completion, ChecklistDefinitionVersion version, ChecklistItem item)
        {
            if (!item.CreateWorkOrderOnFail) return;
            RequireMaintenanceStore();
            if (_uow.Transaction == null || completion.DepartmentId != actor.DepartmentId || version.DepartmentId != actor.DepartmentId || version.Id != completion.VersionId) throw new InvalidOperationException("A checklist failure intent requires its source transaction.");
            if (await _maintenance.FailureAsync(actor.DepartmentId, completion.Id, item.Id) != null) return;
            var intent = New<WorkOrderFailureIntent>(actor);
            intent.CompletionId = completion.Id; intent.ItemId = item.Id; intent.VersionId = version.Id; intent.OccurrenceId = completion.OccurrenceId;
            intent.AuthorizedBy = version.CreatedBy; intent.TargetType = completion.TargetType; intent.TargetId = completion.TargetId; intent.TargetGroupId = completion.TargetGroupId;
            intent.Priority = (int)item.WorkOrderPriority; intent.HoldUnit = item.SetUnitStateOnFail; intent.HoldAsset = item.HoldAssetOnFail;
            await _store.AllocateAsync(intent); // No answers, findings, copied text, billing lookup or paid side effect.
        }
        private async Task<WorkOrderInput> AutomatedTargetAsync(ChecklistActor owner, int? unitId, int? groupId, string assetId)
        {
            await _authorization.RequireMemberAsync(owner);
            var target = new WorkOrderInput { TargetUnitId = unitId, TargetGroupId = groupId, InventoryAssetId = assetId };
            if (assetId != null)
            {
                var asset = _maintenanceAssets == null ? null : await _maintenanceAssets.RoutingAsync(owner.DepartmentId, assetId);
                if (asset?.DepartmentId != owner.DepartmentId || !await _maintenanceAssets.CanReceiveReminderAsync(owner.DepartmentId, owner.UserId, assetId)) throw new WorkOrderException(404, "TargetUnavailable");
                target.TargetUnitId = asset.UnitId; target.TargetGroupId = asset.GroupId;
            }
            else if (unitId.HasValue)
            {
                var unit = _maintenanceUnits == null ? null : await _maintenanceUnits.GetUnitByIdAsync(unitId.Value);
                if (unit?.DepartmentId != owner.DepartmentId) throw new WorkOrderException(404, "TargetUnavailable");
                target.TargetGroupId = unit.StationGroupId;
            }
            if (!await _authorization.CanManageAsync(owner, target.TargetGroupId)) throw new WorkOrderException(403, "PermissionRequired");
            return target;
        }
        private async Task<List<long>> WorkerTransactionAsync(int departmentId, Func<List<long>, Task> work)
        {
            RequireMaintenanceStore();
            if (_uow.Transaction != null) throw new InvalidOperationException("Maintenance workers own their transaction.");
            var events = new List<long>();
            try
            {
                await _uow.CreateOrGetConnectionAsync(CancellationToken.None); await _store.LockDepartmentAsync(departmentId);
                if (await _access.CanUseMaintenanceAsync(departmentId)) await work(events);
                _uow.CommitChanges();
            }
            catch { _uow.DiscardChanges(); throw; }
            await _outbox.DispatchAfterCommitAsync(events); return events;
        }
        private async Task ProcessFailureAsync(int departmentId, int intentId, WorkOrderMaintenanceSweep result)
        {
            await WorkerTransactionAsync(departmentId, async events =>
            {
                var intent = await _store.GetAsync<WorkOrderFailureIntent>(departmentId, intentId);
                if (intent == null || intent.ProcessedOn.HasValue) return;
                var completion = _checklists == null ? null : await _checklists.GetAsync<ChecklistCompletion>(departmentId, intent.CompletionId);
                // A submitted failure requires prompt restriction even while its independent checklist witness is pending.
                if ((completion == null || completion.State != (int)ChecklistRunState.Submitted && completion.State != (int)ChecklistRunState.AwaitingWitness) || completion.VersionId != intent.VersionId) { result.Deferred++; return; }
                var owner = new ChecklistActor { DepartmentId = departmentId, UserId = intent.AuthorizedBy };
                var unit = intent.TargetType == (int)ChecklistTargetType.Unit && int.TryParse(intent.TargetId, out var u) ? (int?)u : null;
                var target = await AutomatedTargetAsync(owner, unit, intent.TargetGroupId, intent.TargetType == (int)ChecklistTargetType.InventoryAsset ? intent.TargetId : null);
                var request = MaintenanceIdentity("failure:" + departmentId + ":" + intent.CompletionId + ":" + intent.ItemId);
                var order = await _store.RequestAsync(departmentId, request);
                if (order == null)
                {
                    order = New<WorkOrder>(new ChecklistActor { DepartmentId = departmentId, UserId = completion.CreatedBy });
                    order.RequestId = request; order.NumberYear = Now.Year; order.NumberSequence = await _store.NextNumberAsync(departmentId, order.NumberYear);
                    order.SourceType = 1; order.SourceChecklistCompletionId = intent.CompletionId; order.SourceChecklistItemId = intent.ItemId; order.SourceOccurrenceId = intent.OccurrenceId;
                    order.Priority = intent.Priority; order.TargetUnitId = target.TargetUnitId; order.TargetGroupId = target.TargetGroupId; order.InventoryAssetId = target.InventoryAssetId;
                    await _store.AllocateAsync(order); await EventAsync(order, WorkflowTriggerEventType.WorkOrderCreated, events); result.Generated++;
                }
                if (intent.HoldUnit && order.TargetUnitId.HasValue) { await CreateHoldAsync(owner, order, true, null, events, true); result.Held++; }
                if (intent.HoldAsset && order.InventoryAssetId != null) { await CreateHoldAsync(owner, order, false, null, events, true); result.Held++; }
                intent.WorkOrderId = order.Id; intent.ProcessedOn = Now; intent.UpdatedOn = Now; intent.Revision++; await _store.WriteAsync(intent);
            });
        }
        public async Task<List<WorkOrderHoldView>> HoldsAsync(ChecklistActor actor, int orderId)
        {
            RequireMaintenanceStore(); await ReadOrderAsync(actor, orderId);
            return (await ChildrenAsync<WorkOrderSafetyHold>(actor, orderId)).Select(h => new WorkOrderHoldView { Hold = h, Content = Decode<WorkOrderHoldContent>(h.Content) }).ToList();
        }
        public async Task AddHoldAsync(ChecklistActor actor, int orderId, WorkOrderHoldInput input)
        {
            if (input == null || !input.Unit && !input.Asset) throw new WorkOrderException(400, "InvalidInput");
            Text(input.Reason, 4000, true); RequireMaintenanceStore();
            await TransactionAsync(actor, async events =>
            {
                var order = await ReadOrderAsync(actor, orderId); Revision(order, input.Revision);
                if (Terminal(order) || !await _authorization.CanManageAsync(actor, order.TargetGroupId)) throw new WorkOrderException(403, "PermissionRequired");
                if (input.Unit) await CreateHoldAsync(actor, order, true, input.Reason, events, false);
                if (input.Asset) await CreateHoldAsync(actor, order, false, input.Reason, events, false);
                await ChangedAsync(actor, order, WorkOrderActivityType.SafetyHold, events, input.Reason); return true;
            });
        }
        private async Task CreateHoldAsync(ChecklistActor actor, WorkOrder order, bool unit, string reason, List<long> events, bool unattended)
        {
            if (unit && !order.TargetUnitId.HasValue || !unit && order.InventoryAssetId == null) throw new WorkOrderException(400, "TargetUnavailable");
            var holds = await _maintenance.ActiveHoldsAsync(actor.DepartmentId, unit ? order.TargetUnitId : null, unit ? null : order.InventoryAssetId);
            if (holds.Any(h => h.WorkOrderId == order.Id)) return;
            var hold = New<WorkOrderSafetyHold>(actor, order.Id);
            hold.UnitId = unit ? order.TargetUnitId : null; hold.AssetId = unit ? null : order.InventoryAssetId;
            if (unit)
            {
                await AutomatedTargetAsync(actor, order.TargetUnitId, order.TargetGroupId, null);
                var previous = await _maintenance.LatestUnitStateAsync(actor.DepartmentId, order.TargetUnitId.Value);
                var inherited = holds.FirstOrDefault(h => h.AppliedStateId == previous?.UnitStateId);
                hold.PreviousState = inherited?.PreviousState ?? previous?.State;
                if (previous?.State == (int)UnitStateTypes.OutOfService) hold.AppliedStateId = inherited?.AppliedStateId; // Never take ownership of a manual restriction.
                else hold.AppliedStateId = await _maintenance.AppendUnitStateAsync(actor.DepartmentId, order.TargetUnitId.Value, (int)UnitStateTypes.OutOfService, Now);
            }
            else
            {
                if (_inventoryMaintenance == null) throw new WorkOrderException(503, "MaintenanceUnavailable");
                var applied = await _inventoryMaintenance.Value.ApplyHoldAsync(InventoryActor(actor), order.Id, order.InventoryAssetId);
                events.AddRange(applied.OutboxIds);
                var inherited = holds.FirstOrDefault(h => h.AppliedAssetRevision == applied.Revision);
                hold.PreviousState = inherited?.PreviousState ?? applied.State;
                hold.AppliedAssetRevision = applied.State == (int)InventoryAssetStatus.OutForRepair ? inherited?.AppliedAssetRevision : applied.Revision;
            }
            if (unattended) await _store.AllocateAsync(hold);
            else { hold.Content = JsonConvert.SerializeObject(new WorkOrderHoldContent { Reason = reason }); await SaveAsync(actor, hold, true); }
            await MaintenanceEventAsync(order, WorkflowTriggerEventType.WorkOrderSafetyHoldApplied, events, holdId: hold.Id);
        }
        public async Task ReleaseHoldAsync(ChecklistActor actor, int holdId, WorkOrderReleaseInput input)
        {
            if (input == null) throw new WorkOrderException(400, "InvalidInput");
            Text(input.Evidence, 4000, true); Text(input.Qualification, 2000, true); RequireMaintenanceStore();
            await TransactionAsync(actor, async events =>
            {
                var hold = await _store.GetAsync<WorkOrderSafetyHold>(actor.DepartmentId, holdId);
                if (hold?.WorkOrderId == null) throw new WorkOrderException(404, "Unavailable");
                var order = await ReadOrderAsync(actor, hold.WorkOrderId.Value); await RevealAsync(actor, hold); Revision(hold, input.Revision);
                if (hold.ReleasedOn.HasValue || !await _authorization.CanManageAsync(actor, order.TargetGroupId)) throw new WorkOrderException(409, "PermissionRequired");
                if (actor.UserId == order.CompletedBy || actor.UserId == hold.CreatedBy) throw new WorkOrderException(403, "IndependentReleaseRequired");
                var others = (await _maintenance.ActiveHoldsAsync(actor.DepartmentId, hold.UnitId, hold.AssetId)).Where(h => h.Id != hold.Id).ToList();
                if (input.RestoreState && others.Count == 0 && hold.PreviousState.HasValue)
                {
                    if (hold.UnitId.HasValue && hold.AppliedStateId.HasValue)
                    {
                        var current = await _maintenance.LatestUnitStateAsync(actor.DepartmentId, hold.UnitId.Value);
                        if (current?.UnitStateId == hold.AppliedStateId && current.State == (int)UnitStateTypes.OutOfService && await _maintenance.LastAppendedUnitStateIdAsync(actor.DepartmentId, hold.UnitId.Value) == hold.AppliedStateId)
                        { await AutomatedTargetAsync(actor, hold.UnitId, order.TargetGroupId, null); await _maintenance.AppendUnitStateAsync(actor.DepartmentId, hold.UnitId.Value, hold.PreviousState.Value, Now); hold.StateRestored = true; }
                    }
                    else if (hold.AssetId != null && hold.AppliedAssetRevision.HasValue && _inventoryMaintenance != null)
                    {
                        var restored = await _inventoryMaintenance.Value.ApplyHoldAsync(InventoryActor(actor), order.Id, hold.AssetId, hold.PreviousState, hold.AppliedAssetRevision, true);
                        hold.StateRestored = restored != null; if (restored != null) events.AddRange(restored.OutboxIds);
                    }
                }
                var content = Decode<WorkOrderHoldContent>(hold.Content); content.ReleaseEvidence = input.Evidence; content.Qualification = input.Qualification;
                hold.Content = JsonConvert.SerializeObject(content); hold.ReleasedOn = Now; hold.ReleasedBy = actor.UserId; hold.Revision++; await SaveAsync(actor, hold);
                await ChangedAsync(actor, order, WorkOrderActivityType.SafetyReleased, events, input.Evidence);
                await MaintenanceEventAsync(order, WorkflowTriggerEventType.WorkOrderSafetyHoldReleased, events, holdId: hold.Id); return true;
            }, safetyRelease: true);
        }
        private async Task MaintenanceEventAsync(WorkOrder order, WorkflowTriggerEventType trigger, List<long> events, int? holdId = null, int? recurrenceId = null)
        {
            var entry = await _outbox.EnqueueAsync(order.DepartmentId, "WorkOrders", new DomainEventEnvelope { EventName = trigger.ToString(), AggregateType = "WorkOrder", AggregateId = Key(order), AggregateVersion = order.Revision, Trigger = trigger, OccurredOn = Now,
                Payload = new { WorkOrderId = order.Id, order.Revision, order.Status, order.Priority, order.TargetUnitId, order.TargetGroupId, order.InventoryAssetId, order.AssignedToRoleId, order.DueOn, HoldId = holdId, RecurrenceId = recurrenceId } });
            events.Add(entry.DomainEventOutboxId);
        }
    }
}
