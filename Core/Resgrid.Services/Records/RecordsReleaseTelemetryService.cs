using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// The RMS-4 release telemetry baseline (RMS plan section 6, RMS-4). Every number here already exists in a table
	/// the platform writes for its own reasons; this service only counts, so a department administrator, the health
	/// endpoint and the daily worker line all see the same figures. No record content is read.
	/// </summary>
	public class RecordsReleaseTelemetryService : IRecordsReleaseTelemetryService
	{
		/// <summary>RMS plan section 5.12: alert at a sustained 60-second outbox backlog.</summary>
		public const int OutboxLagAlertSeconds = 60;
		public const int PermitNoticeDays = 30;
		private const int WorkflowRunPage = 500;

		private readonly IRecordsCutoverService _cutover;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IDomainEventOutboxRepository _outbox;
		private readonly IWorkflowRunRepository _workflowRuns;
		private readonly IRmsRecordAttachmentsRepository _attachments;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsRecordDueStatesRepository _dueStates;
		private readonly IRmsSubmissionsRepository _submissions;
		private readonly IRmsInspectionsRepository _inspections;
		private readonly IRmsViolationsRepository _violations;
		private readonly IRmsPermitsRepository _permits;
		private readonly IRmsHydrantsRepository _hydrants;
		private readonly IDepartmentDataProtectionService _dataProtection;
		private readonly RecordsPreventionGate _gate;

		public RecordsReleaseTelemetryService(IRecordsCutoverService cutover, IRecordsAuthorizationService authorization, IRmsAccessAuditsRepository audits, IDomainEventOutboxRepository outbox,
			IWorkflowRunRepository workflowRuns, IRmsRecordAttachmentsRepository attachments, IRmsOperationalRecordsRepository records, IRmsRecordDueStatesRepository dueStates, IRmsSubmissionsRepository submissions,
			IRmsInspectionsRepository inspections, IRmsViolationsRepository violations, IRmsPermitsRepository permits, IRmsHydrantsRepository hydrants, IDepartmentDataProtectionService dataProtection, RecordsPreventionGate gate)
		{
			_cutover = cutover; _authorization = authorization; _audits = audits; _outbox = outbox; _workflowRuns = workflowRuns; _attachments = attachments; _records = records; _dueStates = dueStates;
			_submissions = submissions; _inspections = inspections; _violations = violations; _permits = permits; _hydrants = hydrants; _dataProtection = dataProtection; _gate = gate;
		}

		public async Task<RecordsReleaseTelemetry> GetAsync(int departmentId, string userId, int windowHours = 24)
		{
			if (!await _authorization.IsDepartmentAdminAsync(userId, departmentId)) throw new UnauthorizedAccessException("Release telemetry is for department administrators.");
			return await BuildAsync(departmentId, windowHours);
		}

		public async Task<RecordsReleaseTelemetry> LogSnapshotAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var snapshot = await BuildAsync(departmentId, 24);
			Logging.LogInfo(JsonConvert.SerializeObject(new { rms_release_telemetry = snapshot }));
			return snapshot;
		}

		private async Task<RecordsReleaseTelemetry> BuildAsync(int departmentId, int windowHours)
		{
			var window = Math.Clamp(windowHours, 1, 24 * 30);
			var now = DateTime.UtcNow;
			var since = now.AddHours(-window);
			var t = new RecordsReleaseTelemetry { DepartmentId = departmentId, GeneratedOn = now, WindowHours = window };

			var state = await _cutover.GetModuleStateAsync(departmentId);
			t.RecordsActive = state?.RecordsUsable ?? false;
			t.ActivatedOn = state?.ActivatedOn;

			await Try(t, "audits", async () =>
			{
				t.LegacyWriteAttempts = await _audits.CountByActionSinceAsync(departmentId, (int)RmsAccessAuditAction.LegacyWriteDenied, since);
				t.AuthorizationDenials = await _audits.CountByActionSinceAsync(departmentId, (int)RmsAccessAuditAction.Denied, since);
			});
			await Try(t, "outbox", async () =>
			{
				t.OutboxPending = await _outbox.CountByStateForDepartmentAsync(departmentId, (int)DomainEventOutboxState.Pending);
				t.OutboxFailed = await _outbox.CountByStateForDepartmentAsync(departmentId, (int)DomainEventOutboxState.Failed);
				var oldest = await _outbox.GetOldestPendingCreatedOnForDepartmentAsync(departmentId);
				t.OutboxOldestPendingSeconds = oldest.HasValue ? Math.Max(0, (now - oldest.Value).TotalSeconds) : (double?)null;
				t.OutboxLagAlert = t.OutboxPending > 0 && t.OutboxOldestPendingSeconds >= OutboxLagAlertSeconds;
			});
			await Try(t, "workflow runs", async () =>
			{
				// One bounded page of the most recent runs; a department with more than 500 runs in the window is already visible on the Workflows health page.
				var runs = ((await _workflowRuns.GetByDepartmentIdPagedAsync(departmentId, 1, WorkflowRunPage)) ?? Enumerable.Empty<WorkflowRun>()).Where(r => r.StartedOn >= since || r.QueuedOn >= since).ToList();
				t.WorkflowRunsCompleted = runs.Count(r => r.Status == (int)WorkflowRunStatus.Completed);
				t.WorkflowRunsSkipped = runs.Count(r => r.Status == (int)WorkflowRunStatus.Skipped);
				t.WorkflowRunsFailed = runs.Count(r => r.Status == (int)WorkflowRunStatus.Failed);
				t.WorkflowRunsRunning = runs.Count(r => r.Status == (int)WorkflowRunStatus.Running || r.Status == (int)WorkflowRunStatus.Pending || r.Status == (int)WorkflowRunStatus.Retrying);
			});
			await Try(t, "attachments", async () =>
			{
				t.AttachmentsScanPending = await _attachments.CountByScanStateAsync(departmentId, (int)RmsAttachmentScanState.Pending);
				t.AttachmentsScanRejected = await _attachments.CountByScanStateAsync(departmentId, (int)RmsAttachmentScanState.Rejected);
			});
			await Try(t, "records", async () =>
			{
				t.RecordsCreated = await _records.CountCreatedSinceAsync(departmentId, since);
				t.RecordsFinalized = await _records.CountFinalizedSinceAsync(departmentId, since);
				t.RecordsOverdue = await _dueStates.CountOverdueAsync(departmentId);
			});
			await Try(t, "submissions", async () =>
			{
				t.SubmissionsFailed = await _submissions.CountByStateAsync(departmentId, (int)RmsSubmissionState.Failed);
				t.SubmissionsAwaiting = await _submissions.CountByStateAsync(departmentId, (int)RmsSubmissionState.Queued) + await _submissions.CountByStateAsync(departmentId, (int)RmsSubmissionState.InFlight) + await _submissions.CountByStateAsync(departmentId, (int)RmsSubmissionState.AwaitingDestination);
			});
			await Try(t, "prevention", async () =>
			{
				if (await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Inspections))
				{
					t.InspectionsDue = await _inspections.CountAsync(departmentId, new RmsInspectionQuery { States = new List<int> { (int)RmsInspectionState.Scheduled }, ScheduledBefore = now });
					t.ViolationsOverdue = await _violations.CountOverdueAsync(departmentId, now);
				}
				if (await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Permits))
					t.PermitsExpiringSoon = await _permits.CountExpiringAsync(departmentId, now, now.AddDays(PermitNoticeDays));
				if (await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Hydrants))
					t.HydrantsOutOfService = await _hydrants.CountOutOfServiceAsync(departmentId);
			});
			await Try(t, "protection", async () =>
			{
				t.ProtectionEnforced = await _dataProtection.IsProtectionEnforcedAsync(departmentId);
				t.ProtectedCatalogVersion = await _dataProtection.GetPinnedCatalogVersionAsync(departmentId);
			});
			t.SearchEnabled = Config.SearchConfig.Enabled;

			if (t.OutboxLagAlert) t.Alerts.Add($"Outbox backlog: {t.OutboxPending} pending, oldest {Math.Round(t.OutboxOldestPendingSeconds ?? 0)} s (threshold {OutboxLagAlertSeconds} s).");
			if (t.OutboxFailed > 0) t.Alerts.Add($"{t.OutboxFailed} outbox event(s) exhausted their retries.");
			if (t.LegacyWriteAttempts > 0) t.Alerts.Add($"{t.LegacyWriteAttempts} legacy Log/UnitLog write attempt(s) were denied in the last {window} h.");
			if (t.SubmissionsFailed > 0) t.Alerts.Add($"{t.SubmissionsFailed} NERIS submission(s) failed.");
			if (t.AttachmentsScanRejected > 0) t.Warnings.Add($"{t.AttachmentsScanRejected} attachment(s) were rejected by the scanner.");
			if (t.AttachmentsScanPending > 0) t.Warnings.Add($"{t.AttachmentsScanPending} attachment(s) are still waiting for a scan.");
			if (t.RecordsOverdue > 0) t.Warnings.Add($"{t.RecordsOverdue} record obligation(s) are overdue.");
			if (t.AuthorizationDenials > 0) t.Warnings.Add($"{t.AuthorizationDenials} Records action(s) were denied in the last {window} h.");
			if (t.ViolationsOverdue > 0) t.Warnings.Add($"{t.ViolationsOverdue} code violation(s) are past their correction date.");
			if (t.WorkflowRunsFailed > 0) t.Warnings.Add($"{t.WorkflowRunsFailed} Workflow run(s) failed in the last {window} h.");
			return t;
		}

		private static async Task Try(RecordsReleaseTelemetry t, string area, Func<Task> work)
		{
			try { await work(); }
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Release telemetry: {area} counters unavailable for department {t.DepartmentId}.");
				t.Warnings.Add($"{area} counters unavailable.");
			}
		}
	}

	/// <summary>Daily prevention sweep (worker 42): due inspections, overdue violations, permit expiry, hydrant tests due, and the release telemetry line.</summary>
	public class RecordsPreventionSweepService : IRecordsPreventionSweepService
	{
		private readonly IRmsDepartmentCutoversRepository _cutovers;
		private readonly RecordsPreventionGate _gate;
		private readonly IRecordsInspectionsService _inspections;
		private readonly IRecordsPermitsService _permits;
		private readonly IRecordsHydrantsService _hydrants;
		private readonly IRecordsReleaseTelemetryService _telemetry;
		private readonly IRmsOccupanciesRepository _occupancies;
		private readonly IRmsInspectionsRepository _inspectionRows;
		private readonly IRmsViolationsRepository _violationRows;
		private readonly IRmsHydrantsRepository _hydrantRows;
		private readonly IRmsPermitsRepository _permitRows;
		private readonly IRmsCrrActivitiesRepository _crrRows;
		private readonly IRmsInvestigationCasesRepository _caseRows;

		public RecordsPreventionSweepService(IRmsDepartmentCutoversRepository cutovers, RecordsPreventionGate gate, IRecordsInspectionsService inspections, IRecordsPermitsService permits, IRecordsHydrantsService hydrants,
			IRecordsReleaseTelemetryService telemetry, IRmsOccupanciesRepository occupancies, IRmsInspectionsRepository inspectionRows, IRmsViolationsRepository violationRows, IRmsHydrantsRepository hydrantRows,
			IRmsPermitsRepository permitRows, IRmsCrrActivitiesRepository crrRows, IRmsInvestigationCasesRepository caseRows)
		{
			_cutovers = cutovers; _gate = gate; _inspections = inspections; _permits = permits; _hydrants = hydrants; _telemetry = telemetry; _occupancies = occupancies; _inspectionRows = inspectionRows;
			_violationRows = violationRows; _hydrantRows = hydrantRows; _permitRows = permitRows; _crrRows = crrRows; _caseRows = caseRows;
		}

		public async Task<RecordsPreventionSweepResult> SweepAsync(CancellationToken cancellationToken = default)
		{
			var result = new RecordsPreventionSweepResult();
			var now = DateTime.UtcNow;
			foreach (var cutover in (await _cutovers.GetActiveAsync())?.ToList() ?? new List<RmsDepartmentCutover>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				result.DepartmentsEvaluated++;
				try
				{
					var department = await SweepDepartmentAsync(cutover.DepartmentId, now, cancellationToken);
					result.InspectionsGenerated += department.InspectionsGenerated; result.ViolationsBecameOverdue += department.ViolationsBecameOverdue;
					result.PermitsExpiringNotified += department.PermitsExpiringNotified; result.PermitsExpired += department.PermitsExpired; result.HydrantTestsDue += department.HydrantTestsDue; result.Errors += department.Errors;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Prevention sweep failed for department {cutover.DepartmentId}.");
					result.Errors++;
				}
			}
			return result;
		}

		public async Task<RecordsPreventionSweepResult> SweepDepartmentAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new RecordsPreventionSweepResult { DepartmentsEvaluated = 1 };
			if (await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Inspections))
			{
				result.InspectionsGenerated = await _inspections.GenerateDueInspectionsAsync(departmentId, utcNow, cancellationToken);
				result.ViolationsBecameOverdue = await _inspections.EmitOverdueViolationsAsync(departmentId, utcNow, cancellationToken);
			}
			if (await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Permits))
			{
				var (expired, notified) = await _permits.SweepExpiryAsync(departmentId, utcNow, RecordsReleaseTelemetryService.PermitNoticeDays, cancellationToken);
				result.PermitsExpired = expired; result.PermitsExpiringNotified = notified;
			}
			if (await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Hydrants))
				result.HydrantTestsDue = await _hydrants.CountTestDueAsync(departmentId, utcNow);
			try { await _telemetry.LogSnapshotAsync(departmentId, cancellationToken); }
			catch (Exception ex) { Logging.LogException(ex, $"Release telemetry snapshot failed for department {departmentId}."); result.Errors++; }
			return result;
		}

		public async Task<PreventionSummary> GetSummaryAsync(int departmentId, string userId)
		{
			await _gate.RequireViewerAsync(departmentId, userId);
			var now = DateTime.UtcNow;
			var summary = new PreventionSummary();
			if (await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Occupancy))
			{
				summary.Occupancies = await _occupancies.CountLiveAsync(departmentId);
				summary.OccupanciesReviewOverdue = await _occupancies.CountReviewOverdueAsync(departmentId, now);
			}
			if (await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Inspections))
			{
				summary.InspectionsScheduled = await _inspectionRows.CountAsync(departmentId, new RmsInspectionQuery { States = new List<int> { (int)RmsInspectionState.Scheduled, (int)RmsInspectionState.InProgress } });
				summary.InspectionsDue = await _inspectionRows.CountAsync(departmentId, new RmsInspectionQuery { States = new List<int> { (int)RmsInspectionState.Scheduled }, ScheduledBefore = now });
				summary.ViolationsOpen = await _violationRows.CountOpenAsync(departmentId);
				summary.ViolationsOverdue = await _violationRows.CountOverdueAsync(departmentId, now);
			}
			if (await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Hydrants))
			{
				summary.Hydrants = await _hydrantRows.CountLiveAsync(departmentId);
				summary.HydrantsOutOfService = await _hydrantRows.CountOutOfServiceAsync(departmentId);
				summary.HydrantsTestDue = await _hydrants.CountTestDueAsync(departmentId, now);
			}
			if (await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Permits))
			{
				summary.PermitsActive = await _permitRows.CountAsync(departmentId, new RmsPermitQuery { States = new List<int> { (int)RmsPermitState.Issued } });
				summary.PermitsAwaitingReview = await _permitRows.CountAsync(departmentId, new RmsPermitQuery { States = new List<int> { (int)RmsPermitState.Applied, (int)RmsPermitState.UnderReview } });
				summary.PermitsExpiringSoon = await _permitRows.CountExpiringAsync(departmentId, now, now.AddDays(RecordsReleaseTelemetryService.PermitNoticeDays));
			}
			if (await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Crr))
			{
				var rows = ((await _crrRows.GetForRangeAsync(departmentId, now.AddDays(-90), now.AddDays(1), 2000)) ?? Enumerable.Empty<RmsCrrActivity>()).ToList();
				summary.CrrActivitiesLast90Days = rows.Count; summary.CrrSmokeAlarmsLast90Days = rows.Sum(r => r.SmokeAlarmsInstalled);
			}
			if (await _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.Investigations))
				summary.InvestigationCasesOpen = await _caseRows.CountOpenAsync(departmentId);
			return summary;
		}
	}
}
