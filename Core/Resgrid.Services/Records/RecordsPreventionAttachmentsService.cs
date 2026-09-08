using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Files on prevention and investigation aggregates. The pipeline is the record-attachment pipeline (hygiene,
	/// scanner, checksum, ADP seal). Authorization follows the parent: prevention parents need the module flag and
	/// PreventionAdmin to write / Record_View to read; investigation parents need RecordRestricted_View plus active
	/// membership on the owning case, and every read of one is audited.
	/// </summary>
	public class RecordsPreventionAttachmentsService : IRecordsPreventionAttachmentsService
	{
		private readonly RecordsPreventionGate _gate;
		private readonly IRmsPreventionAttachmentsRepository _attachments;
		private readonly IRmsInvestigationCaseMembersRepository _members;
		private readonly IRmsInvestigationEvidenceRepository _evidence;
		private readonly IRecordAttachmentScanner _scanner;
		private readonly IRecordsProtectionService _protection;

		public RecordsPreventionAttachmentsService(RecordsPreventionGate gate, IRmsPreventionAttachmentsRepository attachments, IRmsInvestigationCaseMembersRepository members,
			IRmsInvestigationEvidenceRepository evidence, IRecordAttachmentScanner scanner, IRecordsProtectionService protection)
		{
			_gate = gate; _attachments = attachments; _members = members; _evidence = evidence; _scanner = scanner; _protection = protection;
		}

		private static bool IsInvestigation(RmsPreventionParentKind kind) => kind == RmsPreventionParentKind.InvestigationCase || kind == RmsPreventionParentKind.InvestigationEvidence;

		private static RecordsPreventionModule ModuleOf(RmsPreventionParentKind kind)
		{
			switch (kind)
			{
				case RmsPreventionParentKind.Occupancy: return RecordsPreventionModule.Occupancy;
				case RmsPreventionParentKind.Inspection: case RmsPreventionParentKind.Violation: return RecordsPreventionModule.Inspections;
				case RmsPreventionParentKind.Hydrant: return RecordsPreventionModule.Hydrants;
				case RmsPreventionParentKind.Permit: return RecordsPreventionModule.Permits;
				case RmsPreventionParentKind.CrrActivity: return RecordsPreventionModule.Crr;
				default: return RecordsPreventionModule.Investigations;
			}
		}

		private async Task RequireAccessAsync(int departmentId, string userId, RmsPreventionParentKind kind, string parentId, bool write)
		{
			await _gate.RequireEnabledAsync(departmentId, ModuleOf(kind));
			if (!IsInvestigation(kind))
			{
				if (write) await _gate.RequireAdminAsync(departmentId, userId); else await _gate.RequireViewerAsync(departmentId, userId);
				return;
			}
			await _gate.RequireRestrictedAsync(departmentId, userId);
			var caseId = parentId;
			if (kind == RmsPreventionParentKind.InvestigationEvidence)
				caseId = (await _evidence.GetByIdForDepartmentAsync(departmentId, parentId))?.RmsInvestigationCaseId ?? throw new ArgumentException("The evidence item does not exist.");
			var membership = ((await _members.GetForCaseAsync(departmentId, caseId)) ?? Enumerable.Empty<RmsInvestigationCaseMember>()).FirstOrDefault(m => m.UserId == userId && m.IsActive);
			if (membership == null) throw new UnauthorizedAccessException("Only members of the case may access its files.");
			if (write && membership.Role == (int)RmsInvestigationRole.ReadOnly) throw new UnauthorizedAccessException("A read-only member may not add files.");
		}

		public async Task<RmsPreventionAttachment> AddAsync(int departmentId, string userId, RmsPreventionParentKind parentKind, string parentId, string fileName, string contentType, byte[] data, string description, bool restricted, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(parentId)) throw new ArgumentException("The parent is required.");
			await RequireAccessAsync(departmentId, userId, parentKind, parentId, true);
			var hygiene = RecordAttachmentHygiene.Sanitize(fileName, contentType, data);
			var scan = await _scanner.ScanAsync(hygiene.FileName, hygiene.ContentType, hygiene.Data, cancellationToken);
			if (scan.State == RmsAttachmentScanState.Rejected) throw new RecordAttachmentRejectedException("The file was rejected by the malware scanner.");
			var now = DateTime.UtcNow;
			var attachment = new RmsPreventionAttachment
			{
				RmsPreventionAttachmentId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), ParentKind = (int)parentKind, ParentId = parentId,
				FileName = hygiene.FileName, ContentType = hygiene.ContentType, ByteSize = hygiene.Data.LongLength, Checksum = Checksum(hygiene.Data), Data = hygiene.Data,
				Description = RecordsPreventionGate.Trim(description, 1000), UploadedByUserId = userId, UploadedOn = now, ScanState = (int)scan.State, MetadataStripped = hygiene.MetadataStripped,
				Classification = restricted || IsInvestigation(parentKind) ? (int)RmsEvidenceClassification.Restricted : (int)RmsEvidenceClassification.Unrestricted,
				CreatedOn = now, ModifiedOn = now, RowVersion = 1
			};
			var plaintext = PlaintextSnapshot<RmsPreventionAttachment>.Take(attachment, RmsProtectedFields.PreventionAttachments);
			var bytes = attachment.Data;
			await _protection.ProtectPreventionAttachmentAsync(departmentId, attachment, null, userId, cancellationToken);
			await _attachments.InsertAsync(attachment, cancellationToken, true);
			plaintext.Restore();
			attachment.Data = bytes;
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Attachment added", parentId, new { attachment.RmsPreventionAttachmentId, parentKind = parentKind.ToString(), attachment.ByteSize, attachment.Checksum, scan = scan.State.ToString() }, cancellationToken: cancellationToken);
			return attachment;
		}

		public async Task<List<RmsPreventionAttachment>> GetMetadataAsync(int departmentId, string userId, RmsPreventionParentKind parentKind, string parentId)
		{
			await RequireAccessAsync(departmentId, userId, parentKind, parentId, false);
			var rows = (await _attachments.GetMetadataForParentAsync(departmentId, parentKind, parentId))?.ToList() ?? new List<RmsPreventionAttachment>();
			await _protection.RevealPreventionAttachmentsAsync(departmentId, rows, false);
			return rows;
		}

		public async Task<RmsPreventionAttachment> GetWithDataAsync(int departmentId, string userId, string attachmentId)
		{
			var attachment = await _attachments.GetByIdForDepartmentAsync(departmentId, attachmentId);
			if (attachment == null || attachment.DeletedOn != null) return null;
			var kind = (RmsPreventionParentKind)attachment.ParentKind;
			await RequireAccessAsync(departmentId, userId, kind, attachment.ParentId, false);
			if (attachment.ScanState == (int)RmsAttachmentScanState.Rejected) throw new InvalidOperationException("The file was rejected by the malware scanner and cannot be downloaded.");
			var read = await _protection.RevealPreventionAttachmentsAsync(departmentId, new[] { attachment }, true);
			read.RequireRevealed("attachment download");
			if (IsInvestigation(kind))
				await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Export, "Investigation file downloaded", attachment.ParentId, new { attachmentId, attachment.ByteSize, attachment.Checksum });
			return attachment;
		}

		public async Task RemoveAsync(int departmentId, string userId, string attachmentId, CancellationToken cancellationToken = default)
		{
			var attachment = await _attachments.GetByIdForDepartmentAsync(departmentId, attachmentId);
			if (attachment == null || attachment.DeletedOn != null) throw new ArgumentException("The file does not exist.");
			await RequireAccessAsync(departmentId, userId, (RmsPreventionParentKind)attachment.ParentKind, attachment.ParentId, true);
			attachment.DeletedOn = DateTime.UtcNow; attachment.ModifiedOn = attachment.DeletedOn.Value; attachment.RowVersion++;
			await _attachments.UpdateAsync(attachment, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Change, "Attachment removed", attachment.ParentId, new { attachmentId }, cancellationToken: cancellationToken);
		}

		private static string Checksum(byte[] data)
		{
			using var sha = SHA256.Create();
			return BitConverter.ToString(sha.ComputeHash(data ?? Array.Empty<byte>())).Replace("-", string.Empty).ToLowerInvariant();
		}
	}
}
