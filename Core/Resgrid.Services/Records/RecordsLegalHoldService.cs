using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	public sealed class RecordsLegalHoldService : IRecordsLegalHoldService
	{
		private readonly IRmsRecordLegalHoldsRepository _holds;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsIncidentReportsRepository _reports;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IUnitOfWork _unitOfWork;
		private readonly IRecordsProtectionService _protection;
		private readonly IDomainEventOutboxService _outbox;
		public RecordsLegalHoldService(IRmsRecordLegalHoldsRepository holds, IRmsOperationalRecordsRepository records, IRmsIncidentReportsRepository reports,
			IRecordsAuthorizationService authorization, IRmsAccessAuditsRepository audits, IUnitOfWork unitOfWork, IRecordsProtectionService protection, IDomainEventOutboxService outbox)
		{ _holds = holds; _records = records; _reports = reports; _authorization = authorization; _audits = audits; _unitOfWork = unitOfWork; _protection = protection; _outbox = outbox; }
		private async Task RequireAsync(int departmentId, string userId, string recordId = null)
		{
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ManageRecordLegalHold)
				|| recordId != null && !await _authorization.CanUserViewRecordAsync(userId, recordId, departmentId)) throw new UnauthorizedAccessException();
		}
		public async Task<List<RmsRecordLegalHold>> GetAsync(int departmentId, string userId)
		{
			await RequireAsync(departmentId, userId); var result = new List<RmsRecordLegalHold>();
			foreach (var hold in await _holds.GetAllForDepartmentAsync(departmentId) ?? Enumerable.Empty<RmsRecordLegalHold>())
				if (hold.RecordId == null || await _authorization.CanUserViewRecordAsync(userId, hold.RecordId, departmentId)) result.Add(hold);
			await RequireAsync(departmentId, userId);
			foreach (var hold in result) await RequireAsync(departmentId, userId, hold.RecordId);
			await _protection.RevealLegalHoldsAsync(departmentId, result);
			return result;
		}
		public async Task<RmsRecordLegalHold> PlaceAsync(int departmentId, string userId, RmsRecordLegalHold input, CancellationToken cancellationToken = default)
		{
			await RequireAsync(departmentId, userId);
			if (input == null || string.IsNullOrWhiteSpace(input.Reason) || string.IsNullOrWhiteSpace(input.ReferenceNumber) || string.IsNullOrWhiteSpace(input.Notes)) throw new ArgumentException("Record the hold reason, authority or case reference, and preservation instructions.");
			if (input.Reason.Length > 50 || input.ReferenceNumber.Length > 100 || input.Notes.Length > 4000) throw new ArgumentException("The hold details exceed the permitted length.");
			if (input.PeriodStart > input.PeriodEnd) throw new ArgumentException("The hold end must be on or after its start.");
			var recordId = string.IsNullOrWhiteSpace(input.RecordId) ? null : input.RecordId.Trim();
			var definition = string.IsNullOrWhiteSpace(input.DefinitionKey) ? null : input.DefinitionKey.Trim();
			if (recordId != null && (definition != null || input.PeriodStart.HasValue || input.PeriodEnd.HasValue)) throw new ArgumentException("Choose one record or a definition/date scope.");
			if (definition != null && definition != RmsDefinitionKeys.NerisIncidentReport && !RmsDefinitionKeys.LockedTypes.ContainsKey(definition)) throw new ArgumentException("Choose an available record definition.");
			if (recordId != null)
			{
				await RequireAsync(departmentId, userId, recordId);
				var record = await _records.GetByIdForDepartmentAsync(departmentId, recordId); var report = await _reports.GetByIdForDepartmentAsync(departmentId, recordId);
				if (!(record != null && record.DeletedOn == null && record.PurgedOn == null || report != null && report.DeletedOn == null && report.PurgedOn == null)) throw new ArgumentException("The record is unavailable. A purged record cannot be placed on hold.");
			}
			var now = DateTime.UtcNow;
			var hold = new RmsRecordLegalHold { RmsRecordLegalHoldId = Guid.NewGuid().ToString(), DepartmentId = departmentId, RecordId = recordId, DefinitionKey = definition,
				PeriodStart = input.PeriodStart, PeriodEnd = input.PeriodEnd, Reason = input.Reason.Trim(), ReferenceNumber = input.ReferenceNumber.Trim(), Notes = input.Notes.Trim(), PlacedByUserId = userId, PlacedOn = now, CreatedOn = now, ModifiedOn = now, RowVersion = 1 };
			// The row is sealed in place for storage (ADP catalog v10) and the caller gets its plaintext back afterwards.
			var plaintext = PlaintextSnapshot<RmsRecordLegalHold>.Take(hold, RmsProtectedFields.LegalHolds);
			var notes = hold.Notes;
			long outboxId;
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await RequireAsync(departmentId, userId, recordId);
				await _protection.ProtectLegalHoldAsync(departmentId, hold, null, userId, cancellationToken);
				// Repository shares the retention department/parent lock; placement cannot race a content purge.
				await _holds.InsertAsync(hold, cancellationToken, true);
				outboxId = await EnqueueAsync(hold, WorkflowTriggerEventType.RecordLegalHoldPlaced, cancellationToken);
				await AuditAsync(hold, userId, "Legal hold placed", notes, cancellationToken); _unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
			plaintext.Restore();
			await _outbox.DispatchAfterCommitAsync(new[] { outboxId }, cancellationToken);
			return hold;
		}
		public async Task ReleaseAsync(int departmentId, string userId, string holdId, long expectedVersion, string reason, CancellationToken cancellationToken = default)
		{
			await RequireAsync(departmentId, userId);
			if (string.IsNullOrWhiteSpace(reason) || reason.Length > 4000) throw new ArgumentException("Record the authority and reason for releasing preservation (up to 4,000 characters).");
			var hold = await _holds.GetByIdForDepartmentAsync(departmentId, holdId) ?? throw new ArgumentException("The hold does not exist.");
			await RequireAsync(departmentId, userId, hold.RecordId);
			// ReleaseNotes is a cataloged column written by a targeted UPDATE, so it is sealed on a throwaway row first.
			var sealedNotes = new RmsRecordLegalHold { RmsRecordLegalHoldId = holdId, DepartmentId = departmentId, ReleaseNotes = reason.Trim() };
			await _protection.ProtectLegalHoldAsync(departmentId, sealedNotes, null, userId, cancellationToken);
			var releasedOn = DateTime.UtcNow;
			long outboxId;
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await RequireAsync(departmentId, userId, hold.RecordId);
				if (!await _holds.TryReleaseAsync(departmentId, holdId, expectedVersion, userId, sealedNotes.ReleaseNotes, releasedOn, cancellationToken)) throw new InvalidOperationException("The hold changed or was already released. Reload it before continuing.");
				hold.ReleasedByUserId = userId; hold.ReleasedOn = releasedOn;
				outboxId = await EnqueueAsync(hold, WorkflowTriggerEventType.RecordLegalHoldReleased, cancellationToken);
				await AuditAsync(hold, userId, "Legal hold released", reason.Trim(), cancellationToken); _unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
			await _outbox.DispatchAfterCommitAsync(new[] { outboxId }, cancellationToken);
		}

		/// <summary>legal_hold.* (triggers 156/157): scope, period, reason and actors; never the reference number or the preservation notes.</summary>
		private async Task<long> EnqueueAsync(RmsRecordLegalHold hold, WorkflowTriggerEventType trigger, CancellationToken cancellationToken)
		{
			object recordBlock = new { id = hold.RecordId, kind = (string)null, department_id = hold.DepartmentId };
			// A scope-only hold (definition/date scope, no record) is its own aggregate: the outbox guard must not look its
			// id up as a Record. Holds on a specific record keep the record as the aggregate so purge protection applies.
			var aggregateType = hold.RecordId != null ? DomainEventProducers.RecordsAggregate : DomainEventProducers.LegalHoldAggregate;
			if (hold.RecordId != null)
			{
				var record = await _records.GetByIdForDepartmentAsync(hold.DepartmentId, hold.RecordId);
				if (record != null) recordBlock = RecordsService.RecordBlock(record, null, (RmsRecordState)record.State);
				else
				{
					var report = await _reports.GetByIdForDepartmentAsync(hold.DepartmentId, hold.RecordId);
					if (report != null) { recordBlock = IncidentReportsService.RecordBlock(report, null, (RmsRecordState)report.State); aggregateType = IncidentReportsService.IncidentAggregate; }
				}
			}
			var entry = await _outbox.EnqueueAsync(hold.DepartmentId, DomainEventProducers.Records, new DomainEventEnvelope
			{
				EventName = trigger.ToString(), SchemaVersion = 1, AggregateType = aggregateType, AggregateId = hold.RecordId ?? hold.RmsRecordLegalHoldId, AggregateVersion = (int)hold.RowVersion, Trigger = trigger,
				Payload = new Dictionary<string, object>
				{
					["record"] = recordBlock,
					["legal_hold"] = new
					{
						id = hold.RmsRecordLegalHoldId, record_id = hold.RecordId, definition_key = hold.DefinitionKey, period_start = hold.PeriodStart, period_end = hold.PeriodEnd, reason = hold.Reason,
						placed_by_user_id = hold.PlacedByUserId, placed_on = hold.PlacedOn, released_by_user_id = hold.ReleasedByUserId, released_on = hold.ReleasedOn, is_released = hold.ReleasedOn.HasValue
					},
					["protection"] = IncidentReportsService.ProtectionBlock(await _protection.GetCatalogVersionAsync(hold.DepartmentId))
				},
				CorrelationId = hold.RmsRecordLegalHoldId, OriginClient = RmsOriginClient.Web
			}, cancellationToken);
			return entry.DomainEventOutboxId;
		}
		/// <summary>
		/// The audit row records WHICH hold and WHY in the terms the workflow event already publishes. It never
		/// carries the cataloged text (ReferenceNumber, Notes, ReleaseNotes): RmsAccessAudits is not an ADP-bound
		/// table, so copying that text here would leave the content this service just sealed sitting in the clear
		/// in a second table. The detail checksum still ties the audit row to the exact text that was recorded.
		/// </summary>
		private Task AuditAsync(RmsRecordLegalHold hold, string userId, string purpose, string protectedDetail, CancellationToken ct) => _audits.InsertAsync(new RmsAccessAudit { DepartmentId = hold.DepartmentId, RecordId = hold.RecordId,
			ActorUserId = userId, Action = (int)RmsAccessAuditAction.Admin, Successful = true, OccurredOn = DateTime.UtcNow, Purpose = purpose,
			DetailJson = JsonConvert.SerializeObject(new { hold.RmsRecordLegalHoldId, hold.Reason, detail_checksum = string.IsNullOrEmpty(protectedDetail) ? null : RecordSnapshotSerializer.Checksum(protectedDetail) }) }, ct, true);
	}
}
