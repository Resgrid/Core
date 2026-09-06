using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Worker command 43 (RmsRetentionAndPurgeCommand, RMS plan RMS-3). Three passes per activated department,
	/// each bounded and each independently safe to interrupt:
	/// <list type="number">
	/// <item><b>Retention.</b> Records past their resolved retention period lose their content and keep their
	/// identity — a content-free tombstone (plan section 4.9), so a reference to a purged Record still resolves
	/// to a Record that says it was purged rather than to nothing.</item>
	/// <item><b>Legal hold.</b> Anything an active hold covers is skipped, and the refusal is written to the
	/// access audit. A silent skip would be indistinguishable from a sweep that never ran.</item>
	/// <item><b>Attachment rescan.</b> Attachments left at Pending — stored while the scanner was unreachable
	/// because the department chose availability over fail-closed — are re-submitted to the scanner. Without this
	/// pass a Pending attachment stays unscanned forever, which is the RMS-1 gap this package closes.</item>
	/// </list>
	/// Restricted classes resolve to permanent retention and are therefore never purged by this sweep at all.
	/// </summary>
	public class RecordsRetentionService : IRecordsRetentionService
	{
		public const int MaxRecordsPerDepartment = 500;
		public const int MaxRescansPerDepartment = 100;
		public const string PurgeAuditPurpose = "Retention purge";
		public const string HeldAuditPurpose = "Retention purge refused: legal hold";
		public const string PurgedPlaceholder = "[purged]";

		private readonly IRmsDepartmentCutoversRepository _cutovers;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsIncidentReportsRepository _incidentReports;
		private readonly IRmsOperationalRecordDetailsRepository _details;
		private readonly IRmsRecordAttachmentsRepository _attachments;
		private readonly IRmsRecordLegalHoldsRepository _legalHolds;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IRmsRecordSearchProjectionsRepository _projections;
		private readonly IRecordAttachmentScanner _scanner;
		private readonly IDepartmentSettingsService _settings;
		private readonly IRmsRetentionRepository _purge;

		public RecordsRetentionService(IRmsDepartmentCutoversRepository cutovers, IRmsOperationalRecordsRepository records,
			IRmsIncidentReportsRepository incidentReports, IRmsOperationalRecordDetailsRepository details, IRmsRecordAttachmentsRepository attachments,
			IRmsRecordLegalHoldsRepository legalHolds, IRmsAccessAuditsRepository audits, IRmsRecordSearchProjectionsRepository projections,
			IRecordAttachmentScanner scanner, IDepartmentSettingsService settings, IRmsRetentionRepository purge)
		{
			_cutovers = cutovers;
			_records = records;
			_incidentReports = incidentReports;
			_details = details;
			_attachments = attachments;
			_legalHolds = legalHolds;
			_audits = audits;
			_projections = projections;
			_scanner = scanner;
			_settings = settings;
			_purge = purge;
		}

		public async Task<RecordsRetentionSweepResult> SweepAsync(CancellationToken cancellationToken = default)
		{
			var result = new RecordsRetentionSweepResult();
			var active = (await _cutovers.GetActiveAsync())?.ToList() ?? new List<RmsDepartmentCutover>();

			foreach (var cutover in active)
			{
				cancellationToken.ThrowIfCancellationRequested();
				result.DepartmentsEvaluated++;

				try
				{
					var department = await ProcessDepartmentAsync(cutover.DepartmentId, cancellationToken);
					result.RecordsEvaluated += department.RecordsEvaluated;
					result.RecordsPurged += department.RecordsPurged;
					result.SearchErasuresPending += department.SearchErasuresPending;
					result.AttachmentsPurged += department.AttachmentsPurged;
					result.HeldByLegalHold += department.HeldByLegalHold;
					result.AttachmentsRescanned += department.AttachmentsRescanned;
					result.AttachmentsRejectedOnRescan += department.AttachmentsRejectedOnRescan;
					result.Errors += department.Errors;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Retention sweep failed for department {cutover.DepartmentId}.");
					result.Errors++;
				}
			}

			result.Message = $"{result.DepartmentsEvaluated} departments, {result.RecordsPurged} database content purges, {result.SearchErasuresPending} awaiting committed search erasure, {result.HeldByLegalHold} held, {result.AttachmentsRescanned} rescanned.";
			return result;
		}

		public async Task<RecordsRetentionSweepResult> ProcessDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var result = new RecordsRetentionSweepResult { DepartmentsEvaluated = 1 };
			var now = DateTime.UtcNow;

			var policy = await _settings.GetRecordsRetentionPolicyAsync(departmentId) ?? new RecordsRetentionPolicy();
			var holds = (await _legalHolds.GetActiveForDepartmentAsync(departmentId))?.ToList() ?? new List<RmsRecordLegalHold>();

			// Walk every closed candidate in stable ID order. A permanent/held first page must not starve later records,
			// and history may contain a shorter policy than the one currently displayed in department settings.
			string after = null;
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var page = (await _records.GetRetentionCandidatesAsync(departmentId, now, MaxRecordsPerDepartment, after))?.ToList() ?? new List<RmsOperationalRecord>();
				if (page.Count == 0) break;
				after = page.Last().RmsOperationalRecordId;
				foreach (var record in page)
				{
					result.RecordsEvaluated++;
					await ConsiderOperationalAsync(departmentId, record, policy, holds, now, result, cancellationToken);
				}
				if (page.Count < MaxRecordsPerDepartment) break;
			}
			after = null;
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var page = (await _incidentReports.GetRetentionCandidatesAsync(departmentId, now, MaxRecordsPerDepartment, after))?.ToList() ?? new List<RmsIncidentReport>();
				if (page.Count == 0) break;
				after = page.Last().RmsIncidentReportId;
				foreach (var report in page)
				{
					result.RecordsEvaluated++;
					await ConsiderIncidentReportAsync(departmentId, report, policy, holds, now, result, cancellationToken);
				}
				if (page.Count < MaxRecordsPerDepartment) break;
			}
			await RescanPendingAttachmentsAsync(departmentId, now, result, cancellationToken);
			return result;
		}

		private Task ConsiderOperationalAsync(int departmentId, RmsOperationalRecord record, RecordsRetentionPolicy policy,
			List<RmsRecordLegalHold> holds, DateTime now, RecordsRetentionSweepResult result, CancellationToken cancellationToken)
			=> ConsiderAsync(departmentId, record.RmsOperationalRecordId, RmsRecordKind.Operational, record.RowVersion, now, result, cancellationToken);

		private Task ConsiderIncidentReportAsync(int departmentId, RmsIncidentReport report, RecordsRetentionPolicy policy,
			List<RmsRecordLegalHold> holds, DateTime now, RecordsRetentionSweepResult result, CancellationToken cancellationToken)
			=> ConsiderAsync(departmentId, report.RmsIncidentReportId, RmsRecordKind.IncidentReport, report.RowVersion, now, result, cancellationToken);

		private async Task ConsiderAsync(int departmentId, string recordId, RmsRecordKind kind, long version, DateTime now,
			RecordsRetentionSweepResult result, CancellationToken cancellationToken)
		{
			try
			{
				var outcome = await _purge.PurgeAsync(departmentId, recordId, kind, version, now, cancellationToken);
				if (outcome.Purged) { result.RecordsPurged++; result.AttachmentsPurged += outcome.AttachmentsPurged; }
				if (outcome.SearchErasurePending) result.SearchErasuresPending++;
				if (outcome.Held)
				{
					result.HeldByLegalHold++;
					await AuditAsync(departmentId, recordId, HeldAuditPurpose, new { outcome.Reason }, now, cancellationToken);
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
			catch (Exception ex) { Logging.LogException(ex, "RMS retention purge failed."); result.Errors++; }
		}
		/// <summary>
		/// Re-submits Pending attachments to the scanner. A clean result promotes the row; a rejection deletes the
		/// bytes and marks it Rejected, because a Pending attachment that turns out to be malware has been
		/// downloadable the whole time it sat unscanned. A scanner that is still unreachable leaves the row alone.
		/// </summary>
		private async Task RescanPendingAttachmentsAsync(int departmentId, DateTime now, RecordsRetentionSweepResult result, CancellationToken cancellationToken)
		{
			List<RmsRecordAttachment> pending;
			try
			{
				pending = (await _attachments.GetPendingScanAsync(departmentId, MaxRescansPerDepartment))?.ToList() ?? new List<RmsRecordAttachment>();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Pending attachments could not be listed for department {departmentId}.");
				result.Errors++;
				return;
			}

			foreach (var metadata in pending)
			{
				cancellationToken.ThrowIfCancellationRequested();

				try
				{
					var attachment = await _attachments.GetByIdForDepartmentAsync(departmentId, metadata.RmsRecordAttachmentId);
					if (attachment?.Data == null || attachment.Data.Length == 0)
						continue;

					var scan = await _scanner.ScanAsync(attachment.FileName, attachment.ContentType, attachment.Data, cancellationToken);
					if (scan == null || scan.State == RmsAttachmentScanState.Pending)
						continue;

					if (!await _attachments.ApplyScanResultAsync(departmentId, attachment.RmsRecordAttachmentId, attachment.RowVersion, scan.State, now, cancellationToken))
						continue; // It was deleted, purged or changed while the scanner was working.
					result.AttachmentsRescanned++;
					if (scan.State == RmsAttachmentScanState.Rejected) result.AttachmentsRejectedOnRescan++;
					if (scan.State == RmsAttachmentScanState.Rejected)
						await AuditAsync(departmentId, attachment.RecordId, "Attachment rejected on rescan", new { attachment.RmsRecordAttachmentId, attachment.FileName, scan.Detail }, now, cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Attachment {metadata.RmsRecordAttachmentId} could not be rescanned.");
					result.Errors++;
				}
			}
		}

		/// <summary>
		/// Identity and keys survive a purge; every other text field on the detail row is content and goes. This is
		/// done by reflection deliberately: a typed field added to the detail row later is purged by default rather
		/// than quietly surviving a retention sweep nobody remembered to update.
		/// </summary>
		private static readonly HashSet<string> DetailKeepFields = new HashSet<string>(StringComparer.Ordinal)
		{
			nameof(RmsOperationalRecordDetail.RmsOperationalRecordDetailId),
			nameof(RmsOperationalRecordDetail.ProtectionId),
			nameof(RmsOperationalRecordDetail.RecordId),
			nameof(RmsOperationalRecordDetail.RevisionId),
			nameof(RmsOperationalRecordDetail.TableName),
			nameof(RmsOperationalRecordDetail.IdName)
		};

		public static void BlankContent(RmsOperationalRecordDetail detail)
		{
			if (detail == null)
				return;

			foreach (var property in typeof(RmsOperationalRecordDetail).GetProperties())
			{
				if (property.PropertyType != typeof(string) || !property.CanWrite || DetailKeepFields.Contains(property.Name))
					continue;

				property.SetValue(detail, null);
			}
		}

		private Task AuditAsync(int departmentId, string recordId, string purpose, object detail, DateTime now, CancellationToken cancellationToken)
		{
			return _audits.InsertAsync(new RmsAccessAudit
			{
				DepartmentId = departmentId,
				RecordId = recordId,
				Action = (int)RmsAccessAuditAction.Change,
				ActorUserId = null,
				Purpose = purpose,
				OriginClient = (int)RmsOriginClient.System,
				Successful = true,
				OccurredOn = now,
				DetailJson = detail == null ? null : JsonConvert.SerializeObject(detail)
			}, cancellationToken, true);
		}
	}
}
