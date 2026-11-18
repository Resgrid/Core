using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Deleting an incident report: a soft delete (DeletedOn) that every read path already honours. The report leaves the
	/// queue, the year list, search and the field-app change feed (as a tombstone), and stops holding its Call. The content
	/// stays until worker 43 erases it after <c>RecordsRetentionConfig.DeletedIncidentReportPurgeDays</c>, so a mistaken
	/// delete can be recovered and a legal hold placed afterwards still stops the erasure.
	/// </summary>
	public partial class IncidentReportsService
	{
		/// <summary>Longest delete reason kept in the access audit.</summary>
		public const int DeleteReasonMaxLength = 1000;

		/// <summary>Access-audit purpose of a delete; the detail carries the reason and the state it was deleted from.</summary>
		public const string DeleteAuditPurpose = "Delete";

		public async Task DeleteAsync(int departmentId, string userId, string reportId, string reasonText, RmsOriginClient origin = RmsOriginClient.Web, CancellationToken cancellationToken = default)
		{
			var report = await LoadAsync(departmentId, reportId);
			var from = (RmsRecordState)report.State;
			// Who may close a report out is decided before any policy answer, so a refusal never tells a stranger what the report holds.
			await RequireCloseOutAsync(report, userId);

			if (report.AmendsRevisionId != null)
				throw new IncidentReportDeleteRefusedException(reportId, IncidentReportDeleteRefusal.AmendmentOpen);
			// Never finalized: cancel, then delete. Finalized, amended or with the destination: it is an official record and
			// has to be voided first, which writes its own attested revision.
			var cancelFirst = from != RmsRecordState.Cancelled && from != RmsRecordState.Voided
				&& RmsLifecycle.CanTransition((RmsLifecyclePreset)report.LifecyclePreset, from, RmsRecordState.Cancelled);
			if (from != RmsRecordState.Cancelled && from != RmsRecordState.Voided && !cancelFirst)
				throw new IncidentReportDeleteRefusedException(reportId, IncidentReportDeleteRefusal.NotDeletable);

			var reason = string.IsNullOrWhiteSpace(reasonText) ? null : reasonText.Trim();
			if (reason != null && reason.Length > DeleteReasonMaxLength)
				reason = reason.Substring(0, DeleteReasonMaxLength);
			if (from == RmsRecordState.Voided)
			{
				// A voided report was official once; taking it out of every list is a department decision.
				if (!await _authorization.IsDepartmentAdminAsync(userId, departmentId))
					throw new IncidentReportDeleteRefusedException(reportId, IncidentReportDeleteRefusal.VoidedNeedsDepartmentAdmin);
				if (reason == null)
					throw new IncidentReportDeleteRefusedException(reportId, IncidentReportDeleteRefusal.ReasonRequired);
			}

			// A fast refusal before any lock is taken; the same policy is answered again under the lock below.
			await RefuseByContentPolicyAsync(report, await _analyses.GetForReportAsync(departmentId, reportId));

			var now = DateTime.UtcNow;
			var outboxIds = new List<long>();
			// One transaction for the cancel and the delete: a failure anywhere leaves the draft exactly as it was, with no
			// RecordCancelled event, instead of a cancelled report that freed its Call but never left the lists.
			await InTransactionAsync(async () =>
			{
				// The version the policy above was answered for. An edit since then fails here and the retry asks again, so a
				// draft that gained an exposure after the check is never cancelled and hidden on the strength of the old answer.
				// The bump also takes the department's Records lock, which analysis creation, legal hold placement and content
				// writes take too: whatever committed before it is visible to the re-check, and nothing new lands until commit.
				await GuardVersionAsync(report, report.RowVersion, cancellationToken);
				var analysis = await _analyses.GetForReportAsync(departmentId, reportId);
				await RefuseByContentPolicyAsync(report, analysis);

				if (cancelFirst)
				{
					// Its own lifecycle step (RecordCancelled, 107), exactly as the Cancel button would do it; the event is
					// dispatched only once the delete has committed with it.
					MarkCancelled(report, userId, now);
					await RecordCancelledAsync(report, from, userId, origin, outboxIds, cancellationToken);
				}
				// Audited first: the audit repository treats a deleted record as unavailable and strips the purpose and detail
				// of anything written against it afterwards (so a late audit cannot put content back after a purge).
				await AuditAsync(departmentId, userId, reportId, report.CurrentRevisionId, RmsAccessAuditAction.Change, DeleteAuditPurpose, origin, cancellationToken,
					new { fromState = from.ToString(), cancelledFirst = cancelFirst, reason, analysisId = analysis?.RmsIncidentAnalysisId,
						purgeAfterDays = DeletedPurgeDays() });
				if (analysis != null)
				{
					// Before the report is marked deleted: the analysis bump locks its live parent and would refuse a deleted one.
					// Same compare-and-swap the analysis editor uses, so a save racing the delete loses instead of reviving it.
					var expected = analysis.RowVersion;
					if (!await _analyses.TryBumpRowVersionAsync(departmentId, analysis.RmsIncidentAnalysisId, expected, cancellationToken))
						throw new RecordConcurrencyException(analysis.RmsIncidentAnalysisId, expected, -1);
					analysis.RowVersion = expected + 1;
					analysis.DeletedOn = now;
					analysis.ModifiedOn = now;
					analysis.ModifiedByUserId = userId;
					await _analyses.UpdateAsync(analysis, cancellationToken, true);
					await _submissions.SupersedeOpenForRecordAsync(departmentId, analysis.RmsIncidentAnalysisId, null, now, cancellationToken);
				}
				report.DeletedOn = now;
				report.ModifiedOn = now;
				report.ModifiedByUserId = userId;
				await _reports.UpdateAsync(report, cancellationToken, true);
				// Anything still queued never went out (HasLeftResgridAsync); it must not go out now.
				await _submissions.SupersedeOpenForRecordAsync(departmentId, reportId, null, now, cancellationToken);
				// The projection carries DeletedOn: search drops the document and the change feed sends a tombstone.
				await RefreshProjectionAsync(report, cancellationToken);
			});
			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);
		}

		/// <summary>
		/// Whether the deleting user removed this report, recently enough that an idempotent retry of their delete is still
		/// answerable (<see cref="DeleteReplayWindow"/>). A deleted report no longer loads, so without this a retry whose
		/// first attempt committed but never stored its receipt would hear 404.
		/// </summary>
		public async Task<bool> WasDeletedByAsync(int departmentId, string userId, string reportId)
		{
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(reportId))
				return false;
			var report = await _reports.GetByIdForDepartmentAsync(departmentId, reportId);
			return report?.DeletedOn != null && report.PurgedOn == null && report.DeletedOn.Value > DateTime.UtcNow.Subtract(DeleteReplayWindow)
				&& string.Equals(report.ModifiedByUserId, userId, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>How long a delete stays answerable to its own retry; the same day the API keeps idempotency receipts.</summary>
		public static readonly TimeSpan DeleteReplayWindow = TimeSpan.FromHours(24);

		private async Task RefuseByContentPolicyAsync(RmsIncidentReport report, RmsIncidentAnalysis analysis)
		{
			var reportId = report.RmsIncidentReportId;
			if (await HasLeftResgridAsync(report, analysis))
				throw new IncidentReportDeleteRefusedException(reportId, IncidentReportDeleteRefusal.SubmittedExternally);
			if (await HasPermanentContentAsync(report.DepartmentId, reportId))
				throw new IncidentReportDeleteRefusedException(reportId, IncidentReportDeleteRefusal.PermanentContent);
			if (await IsUnderLegalHoldAsync(report, analysis))
				throw new IncidentReportDeleteRefusedException(reportId, IncidentReportDeleteRefusal.LegalHold);
		}

		/// <summary>Days a deleted report keeps its content; never below one so the delete can always be undone by support.</summary>
		public static int DeletedPurgeDays() => Math.Max(1, Config.RecordsRetentionConfig.DeletedIncidentReportPurgeDays);

		/// <summary>
		/// Whether the report or its analysis reached an outside destination. Deleting it in Resgrid would not remove it there,
		/// and nothing is wired to delete at the destination (NERIS has no withdrawal on void either), so it stays.
		/// </summary>
		private async Task<bool> HasLeftResgridAsync(RmsIncidentReport report, RmsIncidentAnalysis analysis)
		{
			if (!string.IsNullOrWhiteSpace(report.NerisIncidentId) || !string.IsNullOrWhiteSpace(analysis?.NerisAnalysisId))
				return true;
			var ids = new List<string> { report.RmsIncidentReportId };
			if (analysis != null)
				ids.Add(analysis.RmsIncidentAnalysisId);
			foreach (var id in ids)
			{
				var submissions = await _submissions.GetForRecordAsync(report.DepartmentId, id) ?? Enumerable.Empty<RmsSubmission>();
				if (submissions.Any(LeftResgrid))
					return true;
			}
			return false;
		}

		/// <summary>A submission that was ever sent, or whose fate at the destination is unknown, counts as delivered.</summary>
		public static bool LeftResgrid(RmsSubmission submission)
		{
			if (submission == null)
				return false;
			var state = (RmsSubmissionState)submission.State;
			return submission.Attempts > 0 || submission.SentOn.HasValue || !string.IsNullOrWhiteSpace(submission.ExternalId)
				|| submission.RequiresReconciliation || submission.CreatePendingReceipt
				|| state == RmsSubmissionState.InFlight || state == RmsSubmissionState.Accepted || state == RmsSubmissionState.Rejected
				|| state == RmsSubmissionState.AwaitingDestination;
		}

		/// <summary>
		/// Casualty, rescue and exposure entries are kept permanently (the retention purge refuses them too). A delete would
		/// hide them from every list while keeping them forever, so the entries have to be removed from the draft first.
		/// </summary>
		private async Task<bool> HasPermanentContentAsync(int departmentId, string reportId)
		{
			if ((await _casualties.GetForRecordAsync(departmentId, reportId, null))?.Any() == true
				|| (await _exposures.GetForRecordAsync(departmentId, reportId, null))?.Any() == true)
				return true;
			var revisionIds = ((await _revisions.GetForRecordAsync(departmentId, reportId)) ?? Enumerable.Empty<RmsRevision>()).Select(r => r.RmsRevisionId).ToList();
			if (revisionIds.Count == 0)
				return false;
			return (await _casualties.GetForRevisionsAsync(departmentId, revisionIds))?.Any() == true
				|| (await _exposures.GetForRevisionsAsync(departmentId, revisionIds))?.Any() == true;
		}

		private async Task<bool> IsUnderLegalHoldAsync(RmsIncidentReport report, RmsIncidentAnalysis analysis)
		{
			var holds = (await _legalHolds.GetActiveForDepartmentAsync(report.DepartmentId))?.ToList() ?? new List<RmsRecordLegalHold>();
			if (holds.Count == 0)
				return false;
			var started = report.CallCreatedOn ?? report.CreatedOn;
			return holds.Any(h => h.Covers(report.RmsIncidentReportId, report.DefinitionKey, started)
				|| analysis != null && h.Covers(analysis.RmsIncidentAnalysisId, report.DefinitionKey, started));
		}
	}
}
