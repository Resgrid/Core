using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	public sealed class IncidentAttachmentsService : IIncidentAttachmentsService
	{
		private readonly IRmsIncidentReportsRepository _reports;
		private readonly IRmsRecordAttachmentsRepository _attachments;
		private readonly IRmsRevisionsRepository _revisions;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRecordAttachmentScanner _scanner;
		private readonly IUnitOfWork _unitOfWork;
		public IncidentAttachmentsService(IRmsIncidentReportsRepository reports, IRmsRecordAttachmentsRepository attachments, IRmsRevisionsRepository revisions,
			IRmsAccessAuditsRepository audits, IRecordsAuthorizationService authorization, IRecordAttachmentScanner scanner, IUnitOfWork unitOfWork)
		{ _reports = reports; _attachments = attachments; _revisions = revisions; _audits = audits; _authorization = authorization; _scanner = scanner; _unitOfWork = unitOfWork; }

		private async Task<RmsIncidentReport> Authorize(int departmentId, string userId, string reportId, bool write)
		{
			if (!await _authorization.CanUserViewRecordAsync(userId, reportId, departmentId)) throw new UnauthorizedAccessException();
			var report = await _reports.GetByIdForDepartmentAsync(departmentId, reportId);
			if (report == null || report.DeletedOn.HasValue || report.PurgedOn.HasValue) throw new KeyNotFoundException();
			if (write)
			{
				if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.CreateRecord)) throw new UnauthorizedAccessException();
				if (!string.Equals(report.AuthorUserId, userId, StringComparison.Ordinal) && !string.Equals(report.OwnerUserId, userId, StringComparison.Ordinal)
					&& !await _authorization.IsDepartmentAdminAsync(userId, departmentId)
					&& !(report.AmendsRevisionId != null && await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.AmendRecords))) throw new UnauthorizedAccessException();
				if (!(RmsLifecycle.IsEditable((RmsRecordState)report.State) || (RmsRecordState)report.State == RmsRecordState.Rejected || report.AmendsRevisionId != null)
					|| RmsLifecycle.IsTerminal((RmsRecordState)report.State)) throw new InvalidOperationException("Add attachments to a draft or open amendment.");
			}
			return report;
		}

		public async Task<RmsRecordAttachment> AddAsync(int departmentId, string userId, string reportId, long expectedVersion, string fileName, string contentType, byte[] data, string description, CancellationToken cancellationToken = default, int classification = 1)
		{
			var report = await Authorize(departmentId, userId, reportId, true);
			if (!Enum.IsDefined(typeof(RmsEvidenceClassification), classification)) throw new ArgumentException("Choose an attachment classification.");
			if (classification != 0 && !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords)) throw new UnauthorizedAccessException();
			if (report.RowVersion != expectedVersion) throw new RecordConcurrencyException(reportId, expectedVersion, report.RowVersion);
			var clean = RecordAttachmentHygiene.Sanitize(fileName, contentType, data);
			var scan = await _scanner.ScanAsync(clean.FileName, clean.ContentType, clean.Data, cancellationToken) ?? new RecordAttachmentScanResult();
			if (scan.State == RmsAttachmentScanState.Rejected) throw new RecordAttachmentRejectedException("The attachment was rejected by the scanner.");
			var now = DateTime.UtcNow;
			var attachment = new RmsRecordAttachment { RmsRecordAttachmentId = Guid.NewGuid().ToString(), ProtectionId = Guid.NewGuid().ToString(), DepartmentId = departmentId,
				RecordId = reportId, FileName = clean.FileName, ContentType = clean.ContentType, Data = clean.Data, ByteSize = clean.Data.LongLength,
				Checksum = RecordSnapshotSerializer.Checksum(clean.Data), Description = description, UploadedByUserId = userId, UploadedOn = now,
				ScanState = (int)scan.State, MetadataStripped = clean.MetadataStripped, CreatedOn = now, ModifiedOn = now, RowVersion = 1 };
			attachment.Classification = classification;
			_unitOfWork.CreateOrGetConnection();
			try
			{
				report = await Authorize(departmentId, userId, reportId, true);
				if (classification != 0 && !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords)) throw new UnauthorizedAccessException();
				if (report.RowVersion != expectedVersion || !await _reports.TryBumpRowVersionAsync(departmentId, reportId, expectedVersion, cancellationToken))
					throw new RecordConcurrencyException(reportId, expectedVersion, report.RowVersion);
				await _attachments.InsertAsync(attachment, cancellationToken, true);
				await _audits.InsertAsync(new RmsAccessAudit { DepartmentId = departmentId, RecordId = reportId, ActorUserId = userId, Action = (int)RmsAccessAuditAction.Change,
					Purpose = "Incident attachment uploaded", Successful = true, OccurredOn = now, DetailJson = JsonConvert.SerializeObject(new { attachment.RmsRecordAttachmentId, attachment.Checksum, attachment.ByteSize }) }, cancellationToken, true);
				_unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
			// Never clear the object handed to a repository: in-memory stores can retain that instance.
			var metadata = JsonConvert.DeserializeObject<RmsRecordAttachment>(JsonConvert.SerializeObject(attachment)); metadata.Data = null; return metadata;
		}

		public async Task<RmsRecordAttachment> GetAsync(int departmentId, string userId, string reportId, string attachmentId, string revisionId = null)
		{
			await Authorize(departmentId, userId, reportId, false);
			var attachment = revisionId == null ? await _attachments.GetByIdForDepartmentAsync(departmentId, attachmentId) : await _attachments.GetHistoricalByIdForDepartmentAsync(departmentId, attachmentId);
			if (attachment == null || attachment.RecordId != reportId || revisionId == null && attachment.DeletedOn.HasValue) return null;
			if (attachment.ScanState != (int)RmsAttachmentScanState.Clean) throw new InvalidOperationException("The attachment has not passed scanning.");
			if (attachment.RequiresRestrictedAccess && !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords)) throw new UnauthorizedAccessException();
			if (attachment.Data == null || RecordSnapshotSerializer.Checksum(attachment.Data) != attachment.Checksum) throw new InvalidOperationException("Attachment content is unavailable or its checksum does not match.");
			if (revisionId != null)
			{
				var revision = await _revisions.GetByIdForDepartmentAsync(departmentId, revisionId);
				if (revision == null || revision.RecordId != reportId || revision.RecordKind != (int)RmsRecordKind.IncidentReport) return null;
				if (RecordSnapshotSerializer.Checksum(revision.SnapshotJson) != revision.Checksum) throw new InvalidOperationException("The revision checksum does not match.");
				var snapshot = JsonConvert.DeserializeObject<IncidentReportAggregate>(revision.SnapshotJson);
				if (snapshot?.Attachments?.Any(a => a.RmsRecordAttachmentId == attachmentId && a.Checksum == attachment.Checksum) != true) return null;
			}
			return attachment;
		}

		public async Task<bool> RemoveAsync(int departmentId, string userId, string reportId, string attachmentId, long expectedVersion, CancellationToken cancellationToken = default)
		{
			await Authorize(departmentId, userId, reportId, true);
			_unitOfWork.CreateOrGetConnection();
			try
			{
				var report = await Authorize(departmentId, userId, reportId, true);
				if (report.RowVersion != expectedVersion || !await _reports.TryBumpRowVersionAsync(departmentId, reportId, expectedVersion, cancellationToken)) throw new RecordConcurrencyException(reportId, expectedVersion, report.RowVersion);
				var attachment = await _attachments.GetByIdForDepartmentAsync(departmentId, attachmentId);
				if (attachment == null || attachment.RecordId != reportId) { _unitOfWork.DiscardChanges(); return false; }
				if (attachment.RequiresRestrictedAccess && !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords)) throw new UnauthorizedAccessException();
				attachment.DeletedOn = DateTime.UtcNow; attachment.ModifiedOn = attachment.DeletedOn.Value; attachment.RowVersion++;
				await _attachments.UpdateAsync(attachment, cancellationToken, true);
				await _audits.InsertAsync(new RmsAccessAudit { DepartmentId = departmentId, RecordId = reportId, ActorUserId = userId, Action = (int)RmsAccessAuditAction.Change,
					Purpose = "Incident attachment removed from draft", Successful = true, OccurredOn = DateTime.UtcNow, DetailJson = JsonConvert.SerializeObject(new { attachmentId, attachment.Checksum }) }, cancellationToken, true);
				_unitOfWork.CommitChanges(); return true;
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
		}
	}
}
