using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
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
		public RecordsLegalHoldService(IRmsRecordLegalHoldsRepository holds, IRmsOperationalRecordsRepository records, IRmsIncidentReportsRepository reports,
			IRecordsAuthorizationService authorization, IRmsAccessAuditsRepository audits, IUnitOfWork unitOfWork)
		{ _holds = holds; _records = records; _reports = reports; _authorization = authorization; _audits = audits; _unitOfWork = unitOfWork; }
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
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await RequireAsync(departmentId, userId, recordId);
				// Repository shares the retention department/parent lock; placement cannot race a content purge.
				await _holds.InsertAsync(hold, cancellationToken, true);
				await AuditAsync(hold, userId, "Legal hold placed", hold.Notes, cancellationToken); _unitOfWork.CommitChanges(); return hold;
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
		}
		public async Task ReleaseAsync(int departmentId, string userId, string holdId, long expectedVersion, string reason, CancellationToken cancellationToken = default)
		{
			await RequireAsync(departmentId, userId);
			if (string.IsNullOrWhiteSpace(reason) || reason.Length > 4000) throw new ArgumentException("Record the authority and reason for releasing preservation (up to 4,000 characters).");
			var hold = await _holds.GetByIdForDepartmentAsync(departmentId, holdId) ?? throw new ArgumentException("The hold does not exist.");
			await RequireAsync(departmentId, userId, hold.RecordId);
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await RequireAsync(departmentId, userId, hold.RecordId);
				if (!await _holds.TryReleaseAsync(departmentId, holdId, expectedVersion, userId, reason.Trim(), DateTime.UtcNow, cancellationToken)) throw new InvalidOperationException("The hold changed or was already released. Reload it before continuing.");
				await AuditAsync(hold, userId, "Legal hold released", reason.Trim(), cancellationToken); _unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
		}
		private Task AuditAsync(RmsRecordLegalHold hold, string userId, string purpose, string reason, CancellationToken ct) => _audits.InsertAsync(new RmsAccessAudit { DepartmentId = hold.DepartmentId, RecordId = hold.RecordId,
			ActorUserId = userId, Action = (int)RmsAccessAuditAction.Admin, Successful = true, OccurredOn = DateTime.UtcNow, Purpose = purpose, DetailJson = JsonConvert.SerializeObject(new { hold.RmsRecordLegalHoldId, hold.ReferenceNumber, reason }) }, ct, true);
	}
}
