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

		public RecordsRetentionService(IRmsDepartmentCutoversRepository cutovers, IRmsOperationalRecordsRepository records,
			IRmsIncidentReportsRepository incidentReports, IRmsOperationalRecordDetailsRepository details, IRmsRecordAttachmentsRepository attachments,
			IRmsRecordLegalHoldsRepository legalHolds, IRmsAccessAuditsRepository audits, IRmsRecordSearchProjectionsRepository projections,
			IRecordAttachmentScanner scanner, IDepartmentSettingsService settings)
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

			result.Message = $"{result.DepartmentsEvaluated} departments, {result.RecordsPurged} purged, {result.HeldByLegalHold} held, {result.AttachmentsRescanned} rescanned.";
			return result;
		}

		public async Task<RecordsRetentionSweepResult> ProcessDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var result = new RecordsRetentionSweepResult { DepartmentsEvaluated = 1 };
			var now = DateTime.UtcNow;

			var policy = await _settings.GetRecordsRetentionPolicyAsync(departmentId) ?? new RecordsRetentionPolicy();
			var holds = (await _legalHolds.GetActiveForDepartmentAsync(departmentId))?.ToList() ?? new List<RmsRecordLegalHold>();

			// The widest period any definition could still be retained for bounds the candidate query; anything
			// finalized after it cannot be past retention under any policy, so it is not worth loading.
			var longestYears = LongestRetentionYears(policy);
			if (longestYears > 0)
			{
				var cutoff = now.AddYears(-longestYears);

				foreach (var record in (await _records.GetRetentionCandidatesAsync(departmentId, cutoff, MaxRecordsPerDepartment))?.ToList() ?? new List<RmsOperationalRecord>())
				{
					cancellationToken.ThrowIfCancellationRequested();
					result.RecordsEvaluated++;
					await ConsiderOperationalAsync(departmentId, record, policy, holds, now, result, cancellationToken);
				}

				foreach (var report in (await _incidentReports.GetRetentionCandidatesAsync(departmentId, cutoff, MaxRecordsPerDepartment))?.ToList() ?? new List<RmsIncidentReport>())
				{
					cancellationToken.ThrowIfCancellationRequested();
					result.RecordsEvaluated++;
					await ConsiderIncidentReportAsync(departmentId, report, policy, holds, now, result, cancellationToken);
				}
			}

			await RescanPendingAttachmentsAsync(departmentId, now, result, cancellationToken);
			return result;
		}

		/// <summary>
		/// Zero means permanent for the definitions that resolve to it, so it never bounds the query; the answer is
		/// the longest finite period any definition could resolve to, or 0 when everything is permanent.
		/// </summary>
		private static int LongestRetentionYears(RecordsRetentionPolicy policy)
		{
			var years = new List<int>();
			if (policy.DepartmentDefaultYears.HasValue && policy.DepartmentDefaultYears.Value > 0)
				years.Add(policy.DepartmentDefaultYears.Value);
			else if (!policy.DepartmentDefaultYears.HasValue)
				years.Add(RecordsRetentionPolicy.StandardClassDefaultYears);

			foreach (var over in policy.Overrides ?? new List<RecordsRetentionOverride>())
			{
				if (over.RetentionYears > 0)
					years.Add(over.RetentionYears);
			}

			return years.Count == 0 ? 0 : years.Max();
		}

		private async Task ConsiderOperationalAsync(int departmentId, RmsOperationalRecord record, RecordsRetentionPolicy policy,
			List<RmsRecordLegalHold> holds, DateTime now, RecordsRetentionSweepResult result, CancellationToken cancellationToken)
		{
			var years = policy.ResolveYears(record.DefinitionKey);
			if (years <= RecordsRetentionPolicy.Permanent)
				return;

			var expiresOn = (record.FinalizedOn ?? record.CreatedOn).AddYears(years);
			if (now < expiresOn)
				return;

			var hold = holds.FirstOrDefault(h => h.Covers(record.RmsOperationalRecordId, record.DefinitionKey, record.StartedOn ?? record.CreatedOn));
			if (hold != null)
			{
				result.HeldByLegalHold++;
				await AuditAsync(departmentId, record.RmsOperationalRecordId, HeldAuditPurpose, new { hold.RmsRecordLegalHoldId, hold.Reason, hold.ReferenceNumber, expiresOn }, now, cancellationToken);
				return;
			}

			try
			{
				result.AttachmentsPurged += await PurgeAttachmentsAsync(departmentId, record.RmsOperationalRecordId, now, cancellationToken);

				// Content goes; identity, number and lifecycle history stay. This is the tombstone of plan 4.9.
				var detail = await _details.GetDraftAsync(departmentId, record.RmsOperationalRecordId);
				if (detail != null)
				{
					BlankContent(detail);
					detail.ModifiedOn = now;
					await _details.UpdateAsync(detail, cancellationToken, true);
				}

				record.DisplaySummary = PurgedPlaceholder;
				record.PurgedOn = now;
				record.ModifiedOn = now;
				record.RowVersion += 1;
				await _records.UpdateAsync(record, cancellationToken, true);
				await TombstoneProjectionAsync(departmentId, record.RmsOperationalRecordId, now, cancellationToken);

				result.RecordsPurged++;
				await AuditAsync(departmentId, record.RmsOperationalRecordId, PurgeAuditPurpose, new { record.DefinitionKey, years, expiresOn }, now, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Retention purge failed for record {record.RmsOperationalRecordId}.");
				result.Errors++;
			}
		}

		private async Task ConsiderIncidentReportAsync(int departmentId, RmsIncidentReport report, RecordsRetentionPolicy policy,
			List<RmsRecordLegalHold> holds, DateTime now, RecordsRetentionSweepResult result, CancellationToken cancellationToken)
		{
			var years = policy.ResolveYears(report.DefinitionKey);
			if (years <= RecordsRetentionPolicy.Permanent)
				return;

			var expiresOn = (report.FinalizedOn ?? report.CreatedOn).AddYears(years);
			if (now < expiresOn)
				return;

			var hold = holds.FirstOrDefault(h => h.Covers(report.RmsIncidentReportId, report.DefinitionKey, report.CallCreatedOn ?? report.CreatedOn));
			if (hold != null)
			{
				result.HeldByLegalHold++;
				await AuditAsync(departmentId, report.RmsIncidentReportId, HeldAuditPurpose, new { hold.RmsRecordLegalHoldId, hold.Reason, hold.ReferenceNumber, expiresOn }, now, cancellationToken);
				return;
			}

			try
			{
				result.AttachmentsPurged += await PurgeAttachmentsAsync(departmentId, report.RmsIncidentReportId, now, cancellationToken);

				report.DisplaySummary = PurgedPlaceholder;
				report.ModifiedOn = now;
				report.RowVersion += 1;
				await _incidentReports.UpdateAsync(report, cancellationToken, true);
				await TombstoneProjectionAsync(departmentId, report.RmsIncidentReportId, now, cancellationToken);

				result.RecordsPurged++;
				await AuditAsync(departmentId, report.RmsIncidentReportId, PurgeAuditPurpose, new { report.DefinitionKey, years, expiresOn }, now, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Retention purge failed for incident report {report.RmsIncidentReportId}.");
				result.Errors++;
			}
		}

		private async Task<int> PurgeAttachmentsAsync(int departmentId, string recordId, DateTime now, CancellationToken cancellationToken)
		{
			var purged = 0;
			foreach (var metadata in (await _attachments.GetMetadataForRecordAsync(departmentId, recordId))?.ToList() ?? new List<RmsRecordAttachment>())
			{
				var attachment = await _attachments.GetByIdForDepartmentAsync(departmentId, metadata.RmsRecordAttachmentId);
				if (attachment == null)
					continue;

				attachment.Data = null;
				attachment.StorageReference = null;
				attachment.DeletedOn = now;
				attachment.ModifiedOn = now;
				await _attachments.UpdateAsync(attachment, cancellationToken, true);
				purged++;
			}

			return purged;
		}

		private async Task TombstoneProjectionAsync(int departmentId, string recordId, DateTime now, CancellationToken cancellationToken)
		{
			var projection = await _projections.GetByRecordIdAsync(departmentId, recordId);
			if (projection == null)
				return;

			// The queue and the index must stop showing content the department no longer holds.
			projection.DisplaySummary = PurgedPlaceholder;
			projection.SearchText = null;
			projection.ModifiedOn = now;
			await _projections.UpdateAsync(projection, cancellationToken, true);
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

					attachment.ScanState = (int)scan.State;
					if (scan.State == RmsAttachmentScanState.Rejected)
					{
						attachment.Data = null;
						attachment.StorageReference = null;
						attachment.DeletedOn = now;
						result.AttachmentsRejectedOnRescan++;
					}

					attachment.ModifiedOn = now;
					await _attachments.UpdateAsync(attachment, cancellationToken, true);
					result.AttachmentsRescanned++;

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
