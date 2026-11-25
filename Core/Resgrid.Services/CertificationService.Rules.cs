using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Events;

namespace Resgrid.Services
{
	/// <summary>
	/// Status changes made by an outside rule rather than by a person (Training T3 training-backed enforcement): expire a live record, and
	/// put it back exactly as it was. The certification module knows nothing about the rule; the caller keeps the record's previous state
	/// and gives it back. Role membership follows the record through the existing enforcement (the nightly sweep and its grace).
	/// </summary>
	public partial class CertificationService
	{
		public async Task<PersonnelCertification> ExpireCertificationByRuleAsync(int certificationId, int departmentId, DateTime onDate, string reason, string userId,
			CancellationToken cancellationToken = default)
		{
			var (record, type) = await LoadRecordAsync(certificationId, departmentId);
			var live = record.Status is (int)PersonnelCertificationStatuses.Active or (int)PersonnelCertificationStatuses.Trainee or (int)PersonnelCertificationStatuses.PendingVerification;
			if (!live)
				return null;

			var before = Snapshot(record);
			var oldStatus = record.Status;
			record.Status = (int)PersonnelCertificationStatuses.Expired;
			record.StatusChangedOn = DateTime.UtcNow;
			record.StatusChangedByUserId = userId ?? SystemUserId;
			record.StatusReason = string.IsNullOrWhiteSpace(reason) ? "rule" : reason.Trim();
			// The role grace runs from the record's expiry: a rule that expires a record early moves the expiry to today.
			if (!record.ExpiresOn.HasValue || record.ExpiresOn.Value.Date > onDate.Date)
				record.ExpiresOn = onDate.Date;
			await ProtectRecordBeforeSaveAsync(record, departmentId, cancellationToken);
			var saved = await _personnelCertificationRepository.SaveOrUpdateAsync(record, cancellationToken);
			Audit(departmentId, userId ?? SystemUserId, AuditLogTypes.CertificationStatusChanged, before, Snapshot(saved));
			_eventAggregator.SendMessage(new CertificationExpiredEvent { DepartmentId = departmentId, Certification = saved, TypeCode = type?.Code, TypeName = type?.Type });
			_eventAggregator.SendMessage(new CertificationStatusChangedEvent
			{
				DepartmentId = departmentId, Certification = saved, TypeCode = type?.Code, TypeName = type?.Type, OldStatus = oldStatus, NewStatus = saved.Status,
				Reason = record.StatusReason, ChangedByUserId = userId ?? SystemUserId
			});
			await QueueAudienceChangeAsync(departmentId, saved.UserId, cancellationToken);
			return saved;
		}

		public async Task<PersonnelCertification> RestoreCertificationStateAsync(int certificationId, int departmentId, PersonnelCertificationStatuses status, DateTime? expiresOn,
			string reason, string userId, CancellationToken cancellationToken = default)
		{
			var (record, type) = await LoadRecordAsync(certificationId, departmentId);
			var before = Snapshot(record);
			var oldStatus = record.Status;
			record.Status = (int)status;
			record.ExpiresOn = expiresOn;
			record.StatusChangedOn = DateTime.UtcNow;
			record.StatusChangedByUserId = userId ?? SystemUserId;
			record.StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
			await ProtectRecordBeforeSaveAsync(record, departmentId, cancellationToken);
			var saved = await _personnelCertificationRepository.SaveOrUpdateAsync(record, cancellationToken);
			Audit(departmentId, userId ?? SystemUserId, AuditLogTypes.CertificationStatusChanged, before, Snapshot(saved));
			_eventAggregator.SendMessage(new CertificationStatusChangedEvent
			{
				DepartmentId = departmentId, Certification = saved, TypeCode = type?.Code, TypeName = type?.Type, OldStatus = oldStatus, NewStatus = saved.Status,
				Reason = record.StatusReason, ChangedByUserId = userId ?? SystemUserId
			});
			await QueueAudienceChangeAsync(departmentId, saved.UserId, cancellationToken);
			return saved;
		}
	}
}
