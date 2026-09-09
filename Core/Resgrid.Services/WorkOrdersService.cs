using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
	public sealed partial class WorkOrdersService : IWorkOrdersService
	{
		private readonly IWorkOrderRepository _store;
		private readonly IWorkOrderAuthorizationService _authorization;
		private readonly IReadinessAccessService _access;
		private readonly IUnitOfWork _uow;
		private readonly IAuditLogsRepository _audit;
		private readonly IDomainEventOutboxService _outbox;
		private readonly Lazy<IProtectedReadService> _read;
		private readonly Lazy<IProtectedWriteService> _write;
		private readonly IRecordAttachmentScanner _scanner;
		private readonly TimeProvider _clock;
		public WorkOrdersService(IWorkOrderRepository store, IWorkOrderAuthorizationService authorization, IReadinessAccessService access, IUnitOfWork uow,
			IAuditLogsRepository audit, IDomainEventOutboxService outbox, Lazy<IProtectedReadService> read, Lazy<IProtectedWriteService> write, IRecordAttachmentScanner scanner, TimeProvider clock = null)
		{ _store = store; _authorization = authorization; _access = access; _uow = uow; _audit = audit; _outbox = outbox; _read = read; _write = write; _scanner = scanner; _clock = clock ?? TimeProvider.System; }
		private DateTime Now => _clock.GetUtcNow().UtcDateTime;
		private sealed class StoredContent { public WorkOrderContent Fields { get; set; } public string RequestHash { get; set; } }
		private static T Decode<T>(string value) => JsonConvert.DeserializeObject<T>(value ?? "{}");
		private static string Key(WorkOrderRow row) => row.Id.ToString(CultureInfo.InvariantCulture);
		private static void Revision(WorkOrderRow row, int revision) { if (row.Revision != revision) throw new WorkOrderException(409, "Conflict"); }
		private static void Text(string value, int limit, bool required = false) { if (value?.Length > limit || required && string.IsNullOrWhiteSpace(value)) throw new WorkOrderException(400, "InvalidInput"); }
		private static void Money(decimal? value) { if (value < 0 || value > 100000000m || value.HasValue && decimal.Round(value.Value, 2) != value.Value) throw new WorkOrderException(400, "InvalidInput"); }
		private static bool Terminal(WorkOrder row) => row.Status is 6 or 7 or 8 or 9;
		private async Task RequireWriteAsync(ChecklistActor actor)
		{
			await _authorization.RequireMemberAsync(actor);
			if (!await _access.CanUseMaintenanceAsync(actor.DepartmentId)) throw new WorkOrderException(402, "ReadinessProRequired");
		}
		private async Task<WorkOrder> ReadOrderAsync(ChecklistActor actor, int id)
		{
			await _authorization.RequireMemberAsync(actor);
			var row = id > 0 ? await _store.GetAsync<WorkOrder>(actor.DepartmentId, id) : null;
			if (row == null || !(await _authorization.ScopeAsync(actor)).Allows(row)) throw new WorkOrderException(404, "Unavailable");
			return await RevealAsync(actor, row);
		}
		private async Task<T> RevealAsync<T>(ChecklistActor actor, T row) where T : WorkOrderRow
		{
			if (row == null || row.DepartmentId != actor.DepartmentId) throw new WorkOrderException(404, "Unavailable");
			var plain = !string.IsNullOrEmpty(row.Content) && !ProtectedDataEnvelope.HasEnvelopePrefix(row.Content);
			var result = await _read.Value.ResolveRecordsEntitiesForReadAsync(actor.DepartmentId, new[] { (row, Key(row)) }, WorkOrderTables.Fields<T>(), actor.GrantToken, actor.UserId);
			if (result == null || result.RedactedFields.Count > 0 || result.IsProtected && plain) throw new WorkOrderException(403, "ProtectedDataRequired");
			return row;
		}
		private async Task SaveAsync<T>(ChecklistActor actor, T row, bool insert = false) where T : WorkOrderRow
		{
			row.UpdatedOn = Now;
			if (insert) { var content = row.Content; row.Content = null; await _store.AllocateAsync(row); row.Content = content; }
			var result = await _write.Value.PrepareRecordsEntityWriteAsync(actor.DepartmentId, row, (T)null, Key(row), WorkOrderTables.Fields<T>(), () => row.IsProtected = true, actor.GrantToken, actor.UserId, false);
			if (result?.Success != true || result.IsProtected && !ProtectedDataEnvelope.HasEnvelopePrefix(row.Content)) throw new WorkOrderException(403, "ProtectedDataRequired");
			await _store.WriteAsync(row);
		}
		private T New<T>(ChecklistActor actor, int? orderId = null) where T : WorkOrderRow, new() => new T { DepartmentId = actor.DepartmentId, WorkOrderId = orderId, CreatedBy = actor.UserId, CreatedOn = Now, UpdatedOn = Now };
		private async Task<T> TransactionAsync<T>(ChecklistActor actor, Func<List<long>, Task<T>> command)
		{
			await RequireWriteAsync(actor);
			if (_uow.Transaction != null) throw new InvalidOperationException("Work-order commands own their transaction.");
			var events = new List<long>(); T result;
			try
			{
				await _uow.CreateOrGetConnectionAsync(CancellationToken.None); await _store.LockDepartmentAsync(actor.DepartmentId);
				await RequireWriteAsync(actor); result = await command(events); _uow.CommitChanges();
			}
			catch { _uow.DiscardChanges(); throw; }
			await _outbox.DispatchAfterCommitAsync(events); return result;
		}
		private async Task ActivityAsync(ChecklistActor actor, WorkOrder row, WorkOrderActivityType type, string note = null, int? oldStatus = null, int? newStatus = null, WorkOrderContent snapshot = null)
		{
			var activity = New<WorkOrderActivity>(actor, row.Id); activity.ActivityType = (int)type; activity.OldStatus = oldStatus; activity.NewStatus = newStatus;
			activity.Content = JsonConvert.SerializeObject(new { Note = note, Snapshot = snapshot, AssignedToUserId = type == WorkOrderActivityType.Assigned ? row.AssignedToUserId : null, AssignedToRoleId = type == WorkOrderActivityType.Assigned ? row.AssignedToRoleId : null }); await SaveAsync(actor, activity, true);
			var audit = await _audit.InsertAsync(new AuditLog { DepartmentId = actor.DepartmentId, ObjectDepartmentId = actor.DepartmentId, UserId = actor.UserId, ObjectId = Key(row),
				LogType = (int)AuditLogTypes.WorkOrderChanged, LoggedOn = Now, Successful = true, Message = "WorkOrderChanged", ServerName = Environment.MachineName }, CancellationToken.None);
			audit.Data = JsonConvert.SerializeObject(new { row.Id, row.Revision, ActivityId = activity.Id, ActivityType = (int)type, OldStatus = oldStatus, NewStatus = newStatus });
			var protection = await _write.Value.PrepareRecordsEntityWriteAsync(actor.DepartmentId, audit, null, audit.AuditLogId.ToString(CultureInfo.InvariantCulture), ReadinessHistoryFields.Audits, null, actor.GrantToken, actor.UserId, false);
			if (protection?.Success != true || protection.IsProtected && !ProtectedDataEnvelope.HasEnvelopePrefix(audit.Data)) throw new WorkOrderException(403, "ProtectedDataRequired");
			await _audit.UpdateAsync(audit, CancellationToken.None);
		}
		private async Task EventAsync(WorkOrder row, WorkflowTriggerEventType? trigger, List<long> events)
		{
			var entry = await _outbox.EnqueueAsync(row.DepartmentId, "WorkOrders", new DomainEventEnvelope { EventName = trigger?.ToString() ?? "WorkOrderUpdated", AggregateType = "WorkOrder", AggregateId = Key(row), AggregateVersion = row.Revision,
				Trigger = trigger, OccurredOn = Now, CorrelationId = row.RequestId,
				Payload = new { WorkOrderId = row.Id, row.Revision, row.Status, row.Priority, row.TargetUnitId, row.TargetGroupId, row.InventoryAssetId, row.AssignedToRoleId, row.DueOn } });
			events.Add(entry.DomainEventOutboxId);
		}
		private async Task ChangedAsync(ChecklistActor actor, WorkOrder row, WorkOrderActivityType type, List<long> events, string note = null, int? previous = null, WorkflowTriggerEventType? trigger = null)
		{
			var snapshot = type is WorkOrderActivityType.Updated or WorkOrderActivityType.StatusChanged ? Decode<StoredContent>(row.Content).Fields : null;
			row.Revision++; await SaveAsync(actor, row); await ActivityAsync(actor, row, type, note, previous, previous.HasValue ? row.Status : null, snapshot); await EventAsync(row, trigger, events);
		}
		private static void Validate(WorkOrderInput input)
		{
			if (input?.Content == null || !Guid.TryParseExact(input.RequestId, "D", out _) || !Enum.IsDefined(input.Type) || !Enum.IsDefined(input.Priority)) throw new WorkOrderException(400, "InvalidInput");
			var c = input.Content; Text(c.Title, 200, true); Text(c.Description, 20000); Text(c.LocationText, 1000); Text(c.CostCenter, 200);
			foreach (var v in new[] { c.VendorDetails, c.WarrantyReference, c.ProcedureReference, c.ProcedureVersion, c.PermitReference, c.IsolationReference, c.QualifiedPersonnel, c.Resolution, c.Cause, c.VerificationEvidence }) Text(v, 4000);
			if (c.Currency == null || c.Currency.Length != 3 || c.Currency.Any(ch => ch < 'A' || ch > 'Z')) throw new WorkOrderException(400, "InvalidInput");
			Money(c.EstimatedCost); Money(c.ApprovedCost);
			if (c.Steps == null || c.Steps.Count > 100 || c.Steps.Any(s => s == null || string.IsNullOrWhiteSpace(s.Text) || s.Text.Length > 1000)) throw new WorkOrderException(400, "InvalidInput");
			if (input.DueOn.HasValue && (input.DueOn.Value.Year < 2000 || input.DueOn.Value.Year > 2200)) throw new WorkOrderException(400, "InvalidInput");
			if (input.DueOn.HasValue) input.DueOn = DateTime.SpecifyKind(input.DueOn.Value, DateTimeKind.Utc);
		}
		private static void Apply(WorkOrder row, WorkOrderInput input) { row.Type = (int)input.Type; row.Priority = (int)input.Priority; row.TargetUnitId = input.TargetUnitId; row.TargetGroupId = input.TargetGroupId; row.InventoryAssetId = input.InventoryAssetId; row.DueOn = input.DueOn; }
		private static string Fingerprint(WorkOrderInput input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { input.Type, input.Priority, input.TargetUnitId, input.TargetGroupId, input.InventoryAssetId, input.DueOn, input.Content }))));
		public async Task<WorkOrderDetail> CreateAsync(ChecklistActor actor, WorkOrderInput input)
		{
			Validate(input); await RequireWriteAsync(actor); await _authorization.ValidateTargetAsync(actor, input);
			var id = await TransactionAsync(actor, async events =>
			{
				await _authorization.ValidateTargetAsync(actor, input);
				if ((input.Content.ApprovedCost.HasValue || !string.IsNullOrEmpty(input.Content.VerificationEvidence) || !string.IsNullOrEmpty(input.Content.Resolution)) && !await _authorization.CanManageAsync(actor, input.TargetGroupId)) throw new WorkOrderException(403, "PermissionRequired");
				var hash = Fingerprint(input); var existing = await _store.RequestAsync(actor.DepartmentId, input.RequestId);
				if (existing != null)
				{
					if (existing.CreatedBy != actor.UserId) throw new WorkOrderException(409, "Conflict");
					existing = await RevealAsync(actor, existing);
					if (Decode<StoredContent>(existing.Content).RequestHash != hash) throw new WorkOrderException(409, "Conflict");
					return existing.Id;
				}
				var row = New<WorkOrder>(actor); row.RequestId = input.RequestId; row.NumberYear = Now.Year; row.NumberSequence = await _store.NextNumberAsync(actor.DepartmentId, row.NumberYear); Apply(row, input);
				row.Content = JsonConvert.SerializeObject(new StoredContent { Fields = input.Content, RequestHash = hash });
				await SaveAsync(actor, row, true); await ActivityAsync(actor, row, WorkOrderActivityType.Created, snapshot: input.Content); await EventAsync(row, WorkflowTriggerEventType.WorkOrderCreated, events); return row.Id;
			});
			return await GetAsync(actor, id);
		}
		public async Task UpdateAsync(ChecklistActor actor, int id, WorkOrderInput input)
		{
			Validate(input);
			await TransactionAsync(actor, async events =>
			{
				var row = await ReadOrderAsync(actor, id); Revision(row, input.Revision); var manage = await _authorization.CanManageAsync(actor, row.TargetGroupId);
				if (Terminal(row) || row.Status == (int)WorkOrderStatus.Completed || !manage && (row.Status != 0 || row.CreatedBy != actor.UserId)) throw new WorkOrderException(403, "PermissionRequired");
				await _authorization.ValidateTargetAsync(actor, input);
				if (manage && !await _authorization.CanManageAsync(actor, input.TargetGroupId)) throw new WorkOrderException(403, "PermissionRequired");
				var document = Decode<StoredContent>(row.Content);
				if (row.StartedOn.HasValue && (document.Fields.SafetyCritical && !input.Content.SafetyCritical || document.Fields.HazardousWork && !input.Content.HazardousWork)) throw new WorkOrderException(409, "SafetyRequirements");
				if (!manage && (input.Content.ApprovedCost.HasValue && input.Content.ApprovedCost != document.Fields.ApprovedCost || input.Content.Resolution != document.Fields.Resolution || input.Content.VerificationEvidence != document.Fields.VerificationEvidence)) throw new WorkOrderException(403, "PermissionRequired");
				if (!manage) input.Content.ApprovedCost = document.Fields.ApprovedCost;
				document.Fields = input.Content; row.Content = JsonConvert.SerializeObject(document); Apply(row, input);
				await ChangedAsync(actor, row, WorkOrderActivityType.Updated, events); return true;
			});
		}
		public static WorkOrderStatus[] NextStatuses(WorkOrderStatus current) => current switch
		{
			WorkOrderStatus.Requested => new[] { WorkOrderStatus.Accepted, WorkOrderStatus.Rejected, WorkOrderStatus.Duplicate, WorkOrderStatus.Cancelled },
			WorkOrderStatus.Accepted => new[] { WorkOrderStatus.Cancelled },
			WorkOrderStatus.Assigned => new[] { WorkOrderStatus.InProgress, WorkOrderStatus.OnHold, WorkOrderStatus.Cancelled },
			WorkOrderStatus.InProgress => new[] { WorkOrderStatus.OnHold, WorkOrderStatus.Completed, WorkOrderStatus.Cancelled },
			WorkOrderStatus.OnHold => new[] { WorkOrderStatus.InProgress, WorkOrderStatus.Cancelled },
			WorkOrderStatus.Completed => new[] { WorkOrderStatus.Closed, WorkOrderStatus.InProgress },
			WorkOrderStatus.Closed or WorkOrderStatus.Rejected or WorkOrderStatus.Duplicate or WorkOrderStatus.Cancelled => new[] { WorkOrderStatus.Accepted },
			_ => Array.Empty<WorkOrderStatus>()
		};
		public async Task TransitionAsync(ChecklistActor actor, int id, WorkOrderTransition input)
		{
			if (input == null || !Enum.IsDefined(input.Status)) throw new WorkOrderException(400, "InvalidInput");
			Text(input.Reason, 4000); Text(input.Resolution, 4000); Text(input.Cause, 4000); Text(input.VerificationEvidence, 4000);
			await TransactionAsync(actor, async events =>
			{
				var row = await ReadOrderAsync(actor, id); Revision(row, input.Revision); var old = row.Status;
				if (!NextStatuses((WorkOrderStatus)old).Contains(input.Status)) throw new WorkOrderException(409, "InvalidTransition");
				var manage = await _authorization.CanManageAsync(actor, row.TargetGroupId); var contribute = await _authorization.CanContributeAsync(actor, row);
				var technicianStep = input.Status is WorkOrderStatus.InProgress or WorkOrderStatus.OnHold or WorkOrderStatus.Completed;
				if (!manage && (!contribute || !technicianStep || old == (int)WorkOrderStatus.Completed)) throw new WorkOrderException(403, "PermissionRequired");
				var document = Decode<StoredContent>(row.Content); var c = document.Fields;
				if (input.Status is WorkOrderStatus.OnHold or WorkOrderStatus.Cancelled or WorkOrderStatus.Rejected or WorkOrderStatus.Duplicate || Terminal(row) || old == (int)WorkOrderStatus.Completed && input.Status == WorkOrderStatus.InProgress) Text(input.Reason, 4000, true);
				if (input.Status == WorkOrderStatus.Duplicate)
				{
					if (!input.DuplicateOfId.HasValue || input.DuplicateOfId == id) throw new WorkOrderException(400, "InvalidInput");
					var canonical = await ReadOrderAsync(actor, input.DuplicateOfId.Value);
					if (canonical.Status == (int)WorkOrderStatus.Duplicate) throw new WorkOrderException(409, "InvalidTransition");
					row.DuplicateOfId = canonical.Id;
				}
				if (input.Status == WorkOrderStatus.Accepted) { row.TriagedOn = Now; row.ClosedOn = null; row.CompletedOn = null; row.CompletedBy = null; row.VerifiedBy = null; row.DuplicateOfId = null; foreach (var step in c.Steps) step.Completed = false; row.AssignmentAcceptedOn = null; row.AssignmentAcceptedBy = null; c.Resolution = null; c.Cause = null; c.VerificationEvidence = null; }
				if (input.Status == WorkOrderStatus.InProgress)
				{
					if (!row.AssignmentAcceptedOn.HasValue) throw new WorkOrderException(409, "AcceptAssignmentFirst");
					if (c.HazardousWork && new[] { c.ProcedureReference, c.ProcedureVersion, c.PermitReference, c.IsolationReference, c.QualifiedPersonnel }.Any(string.IsNullOrWhiteSpace)) throw new WorkOrderException(409, "SafetyRequirements");
					if (old == (int)WorkOrderStatus.Completed) { foreach (var step in c.Steps) step.Completed = false; c.Resolution = null; c.Cause = null; }
					row.StartedOn ??= Now; row.CompletedOn = null; row.CompletedBy = null; row.ClosedOn = null; row.VerifiedBy = null; c.VerificationEvidence = null;
				}
				if (input.Status == WorkOrderStatus.Completed)
				{
					Text(input.Resolution, 4000, true); Text(input.Cause, 4000, true);
					if (c.Steps.Any(s => !s.Completed) && !input.ConfirmTasksComplete) throw new WorkOrderException(409, "TasksIncomplete");
					foreach (var step in c.Steps) step.Completed = true;
					c.Resolution = input.Resolution; c.Cause = input.Cause; row.CompletedOn = Now; row.CompletedBy = actor.UserId;
				}
				if (input.Status == WorkOrderStatus.Closed)
				{
					Text(input.VerificationEvidence, 4000, true);
					if ((c.SafetyCritical || c.HazardousWork) && row.CompletedBy == actor.UserId) throw new WorkOrderException(403, "IndependentVerificationRequired");
					c.VerificationEvidence = input.VerificationEvidence; row.VerifiedBy = actor.UserId; row.ClosedOn = Now;
				}
				row.Status = (int)input.Status; row.Content = JsonConvert.SerializeObject(document);
				await ChangedAsync(actor, row, WorkOrderActivityType.StatusChanged, events, input.Reason, old, WorkflowTriggerEventType.WorkOrderStatusChanged); return true;
			});
		}
		public async Task AssignAsync(ChecklistActor actor, int id, WorkOrderAssignment input)
		{
			if (input == null) throw new WorkOrderException(400, "InvalidInput");
			await TransactionAsync(actor, async events =>
			{
				var row = await ReadOrderAsync(actor, id); Revision(row, input.Revision);
				if (!await _authorization.CanManageAsync(actor, row.TargetGroupId)) throw new WorkOrderException(403, "PermissionRequired");
				if (row.Status is not (1 or 2 or 3 or 4)) throw new WorkOrderException(409, "InvalidTransition");
				await _authorization.ValidateAssignmentAsync(actor, row, input.UserId, input.RoleId);
				if (row.AssignedToUserId == input.UserId && row.AssignedToRoleId == input.RoleId && row.AssignedOn.HasValue) return true;
				var old = row.Status; row.AssignedToUserId = string.IsNullOrEmpty(input.UserId) ? null : input.UserId; row.AssignedToRoleId = input.RoleId; row.AssignedOn = Now; row.AssignmentAcceptedOn = null; row.AssignmentAcceptedBy = null; row.Status = (int)WorkOrderStatus.Assigned;
				await ChangedAsync(actor, row, WorkOrderActivityType.Assigned, events, previous: old, trigger: WorkflowTriggerEventType.WorkOrderAssigned);
				if (old != row.Status) await EventAsync(row, WorkflowTriggerEventType.WorkOrderStatusChanged, events); return true;
			});
		}
		public async Task AcceptAssignmentAsync(ChecklistActor actor, int id, int revision)
		{
			await TransactionAsync(actor, async events =>
			{
				var row = await ReadOrderAsync(actor, id); Revision(row, revision);
				if (row.Status != (int)WorkOrderStatus.Assigned || !(await _authorization.RecipientsAsync(actor.DepartmentId, row)).Contains(actor.UserId)) throw new WorkOrderException(403, "PermissionRequired");
				if (row.AssignmentAcceptedOn.HasValue) return true;
				row.AssignmentAcceptedOn = Now; row.AssignmentAcceptedBy = actor.UserId; await ChangedAsync(actor, row, WorkOrderActivityType.AssignmentAccepted, events); return true;
			});
		}
		private async Task<WorkOrder> ContributionAsync(ChecklistActor actor, int id, int revision)
		{
			var row = await ReadOrderAsync(actor, id); Revision(row, revision);
			if (Terminal(row) || row.Status == (int)WorkOrderStatus.Completed || !await _authorization.CanContributeAsync(actor, row)) throw new WorkOrderException(403, "PermissionRequired");
			return row;
		}
		public async Task CommentAsync(ChecklistActor actor, int id, int revision, string note)
		{
			Text(note, 10000, true);
			await TransactionAsync(actor, async events => { var row = await ReadOrderAsync(actor, id); Revision(row, revision); if (Terminal(row) || row.CreatedBy != actor.UserId && !await _authorization.CanContributeAsync(actor, row)) throw new WorkOrderException(403, "PermissionRequired"); await ChangedAsync(actor, row, WorkOrderActivityType.Comment, events, note); return true; });
		}
		public async Task AddLaborAsync(ChecklistActor actor, int id, WorkOrderLaborInput input)
		{
			if (input?.Content == null || input.Content.Hours <= 0 || input.Content.Hours > 24 || decimal.Round(input.Content.Hours, 2) != input.Content.Hours || input.WorkDate.Year < 2000 || input.WorkDate > Now.AddDays(1)) throw new WorkOrderException(400, "InvalidInput");
			Money(input.Content.RatePerHour); Text(input.Content.Note, 4000);
			await TransactionAsync(actor, async events =>
			{
				var row = await ContributionAsync(actor, id, input.Revision); var user = string.IsNullOrEmpty(input.UserId) ? actor.UserId : input.UserId;
				await _authorization.RequireMemberAsync(new ChecklistActor { DepartmentId = actor.DepartmentId, UserId = user });
				if (user != actor.UserId && !await _authorization.CanManageAsync(actor, row.TargetGroupId)) throw new WorkOrderException(403, "PermissionRequired");
				var labor = New<WorkOrderLabor>(actor, id); labor.UserId = user; labor.WorkDate = DateTime.SpecifyKind(input.WorkDate, DateTimeKind.Utc); labor.Content = JsonConvert.SerializeObject(input.Content); await SaveAsync(actor, labor, true);
				await ChangedAsync(actor, row, WorkOrderActivityType.LaborAdded, events); return true;
			});
		}
		public async Task AddPartAsync(ChecklistActor actor, int id, WorkOrderPartInput input)
		{
			if (input?.Content == null || input.Content.Quantity <= 0 || input.Content.Quantity > 100000 || decimal.Round(input.Content.Quantity, 3) != input.Content.Quantity) throw new WorkOrderException(400, "InvalidInput");
			Text(input.Content.Description, 2000, true); Money(input.Content.UnitCost); input.Content.VoidReason = null;
			await TransactionAsync(actor, async events => { var row = await ContributionAsync(actor, id, input.Revision); var part = New<WorkOrderPart>(actor, id); part.Content = JsonConvert.SerializeObject(input.Content); await SaveAsync(actor, part, true); await ChangedAsync(actor, row, WorkOrderActivityType.PartAdded, events); return true; });
		}
		public async Task VoidPartAsync(ChecklistActor actor, int id, int partId, int revision, string reason)
		{
			Text(reason, 4000, true);
			await TransactionAsync(actor, async events =>
			{
				var row = await ContributionAsync(actor, id, revision); var part = await _store.GetAsync<WorkOrderPart>(actor.DepartmentId, partId);
				if (part?.WorkOrderId != id || part.VoidedOn.HasValue || part.InventoryTransactionId != null) throw new WorkOrderException(409, "Unavailable");
				await RevealAsync(actor, part); var c = Decode<WorkOrderPartContent>(part.Content); c.VoidReason = reason; part.Content = JsonConvert.SerializeObject(c); part.VoidedOn = Now; part.Revision++; await SaveAsync(actor, part);
				await ChangedAsync(actor, row, WorkOrderActivityType.PartVoided, events, reason); return true;
			});
		}
		public Task<WorkOrderChoices> ChoicesAsync(ChecklistActor actor) => _authorization.ChoicesAsync(actor);
		private static WorkOrderSummary Summary(WorkOrder row, WorkOrderContent c) => new WorkOrderSummary { Id = row.Id, Number = $"WO-{row.NumberYear}-{row.NumberSequence:D6}", Title = c.Title, Status = (WorkOrderStatus)row.Status, Priority = (WorkOrderPriority)row.Priority,
			Revision = row.Revision, UpdatedOn = row.UpdatedOn, CreatedOn = row.CreatedOn, DueOn = row.DueOn, AssignedToUserId = row.AssignedToUserId, AssignedToRoleId = row.AssignedToRoleId, UnitId = row.TargetUnitId, GroupId = row.TargetGroupId, AssetId = row.InventoryAssetId };
		public async Task<WorkOrderPage> ListAsync(ChecklistActor actor, WorkOrderFilter filter)
		{
			if (filter == null || filter.Page < 0 || filter.Page > 10000 || filter.Status.HasValue && !Enum.IsDefined(filter.Status.Value) || filter.Priority.HasValue && !Enum.IsDefined(filter.Priority.Value) || filter.AssetId != null && !Guid.TryParseExact(filter.AssetId, "D", out _)) throw new WorkOrderException(400, "InvalidInput");
			var scope = await _authorization.ScopeAsync(actor); var rows = await _store.ListAsync(actor.DepartmentId, scope, filter);
			var page = new WorkOrderPage { HasMore = rows.Count > 50, CanWrite = await _access.CanUseMaintenanceAsync(actor.DepartmentId) };
			foreach (var row in rows.Take(50))
            {
                await RevealAsync(actor, row); var item = Summary(row, Decode<StoredContent>(row.Content).Fields);
                if (page.CanWrite && row.Status == 0 && await _authorization.CanManageAsync(actor, row.TargetGroupId)) item.QuickStatus = WorkOrderStatus.Accepted;
                if (page.CanWrite && row.Status == 2 && row.AssignmentAcceptedOn.HasValue && await _authorization.CanContributeAsync(actor, row)) item.QuickStatus = WorkOrderStatus.InProgress;
                page.Items.Add(item);
            }
			return page;
		}
		private async Task<List<T>> ChildrenAsync<T>(ChecklistActor actor, int id) where T : WorkOrderRow
		{
			var result = new List<T>();
			for (var skip = 0; ; skip += 500) { var page = await _store.ChildrenAsync<T>(actor.DepartmentId, id, skip); foreach (var row in page) result.Add(await RevealAsync(actor, row)); if (page.Count < 500) return result; if (skip >= 9500) throw new WorkOrderException(400, "HistoryLimit"); }
		}
		public async Task<WorkOrderDetail> GetAsync(ChecklistActor actor, int id)
		{
			var row = await ReadOrderAsync(actor, id); var c = Decode<StoredContent>(row.Content).Fields; var manage = await _authorization.CanManageAsync(actor, row.TargetGroupId); var contribute = await _authorization.CanContributeAsync(actor, row);
			var write = await _access.CanUseMaintenanceAsync(actor.DepartmentId);
			var detail = new WorkOrderDetail { Order = Summary(row, c), Input = new WorkOrderInput { RequestId = row.RequestId, Revision = row.Revision, Type = (WorkOrderType)row.Type, Priority = (WorkOrderPriority)row.Priority, TargetUnitId = row.TargetUnitId, TargetGroupId = row.TargetGroupId, InventoryAssetId = row.InventoryAssetId, DueOn = row.DueOn, Content = c },
				CanWrite = write, CanManage = manage && write, CanEdit = write && !Terminal(row) && row.Status != 5 && (manage || row.Status == 0 && row.CreatedBy == actor.UserId), CanContribute = write && contribute && !Terminal(row) && row.Status != 5,
				CanAccept = write && row.Status == 2 && !row.AssignmentAcceptedOn.HasValue && (await _authorization.RecipientsAsync(actor.DepartmentId, row)).Contains(actor.UserId), ReportedBy = row.CreatedBy, VerifiedBy = row.VerifiedBy, CompletedOn = row.CompletedOn, ClosedOn = row.ClosedOn };
			if (write) detail.Transitions = NextStatuses((WorkOrderStatus)row.Status).Where(s => manage || contribute && row.Status != 5 && s is WorkOrderStatus.InProgress or WorkOrderStatus.OnHold or WorkOrderStatus.Completed).ToArray();
			detail.Activities = (await ChildrenAsync<WorkOrderActivity>(actor, id)).Select(a => { var activity = Decode<WorkOrderActivityView>(a.Content); activity.Id = a.Id; activity.Type = (WorkOrderActivityType)a.ActivityType; activity.UserId = a.CreatedBy; activity.CreatedOn = a.CreatedOn; activity.OldStatus = a.OldStatus; activity.NewStatus = a.NewStatus; return activity; }).ToList();
			detail.Labor = (await ChildrenAsync<WorkOrderLabor>(actor, id)).Select(l => new WorkOrderLaborView { Id = l.Id, UserId = l.UserId, WorkDate = l.WorkDate, Content = Decode<WorkOrderLaborContent>(l.Content) }).ToList();
			detail.Parts = (await ChildrenAsync<WorkOrderPart>(actor, id)).Select(p => new WorkOrderPartView { Id = p.Id, VoidedOn = p.VoidedOn, Content = Decode<WorkOrderPartContent>(p.Content) }).ToList();
			detail.Files = (await ChildrenAsync<WorkOrderFile>(actor, id)).Select(f => new WorkOrderFileView { Id = f.Id, Name = f.Content, ContentType = f.ContentType, Size = f.Size, WithdrawnOn = f.WithdrawnOn }).ToList();
			return detail;
		}
	}
}
