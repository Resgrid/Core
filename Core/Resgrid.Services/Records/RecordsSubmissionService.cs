using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Worker 41 (RmsSubmissionCommand) logic: claim due submissions with a lease, talk to the destination with no
	/// database transaction open (plan 5.3), then persist the immutable response artifact and the outcome in one
	/// short transaction. Outcomes move the report through Submitted / Accepted / Rejected, retry transient
	/// failures with backoff up to MaxAttempts, and raise triggers 109–111 plus the author-targeted notification
	/// 33 on rejection. Trigger 108 is raised when the submission is queued, not here.
	/// </summary>
	public class RecordsSubmissionService : IRecordsSubmissionService
	{
		public const string RejectionNotificationPurpose = "Submission rejected";

		private readonly IRmsSubmissionsRepository _submissions;
		private readonly IRmsIncidentReportsRepository _reports;
		private readonly IRmsRecordSearchProjectionsRepository _projections;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly INerisProfileService _profiles;
		private readonly INerisSubmissionService _delivery;
		private readonly IDomainEventOutboxService _outbox;
		private readonly IOutboundQueueProvider _outboundQueue;
		private readonly IUnitOfWork _unitOfWork;

		public RecordsSubmissionService(IRmsSubmissionsRepository submissions, IRmsIncidentReportsRepository reports, IRmsRecordSearchProjectionsRepository projections,
			IRmsAccessAuditsRepository audits, INerisProfileService profiles, INerisSubmissionService delivery, IDomainEventOutboxService outbox,
			IOutboundQueueProvider outboundQueue, IUnitOfWork unitOfWork)
		{
			_submissions = submissions;
			_reports = reports;
			_projections = projections;
			_audits = audits;
			_profiles = profiles;
			_delivery = delivery;
			_outbox = outbox;
			_outboundQueue = outboundQueue;
			_unitOfWork = unitOfWork;
		}

		public async Task<RecordsSubmissionSweepResult> SweepAsync(CancellationToken cancellationToken = default)
		{
			var result = new RecordsSubmissionSweepResult();
			if (!NerisConfig.Enabled)
			{
				result.Message = "NERIS submission disabled; nothing to do.";
				return result;
			}

			var owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
			var claimed = (await _submissions.ClaimDueBatchAsync(owner, TimeSpan.FromSeconds(Math.Max(30, NerisConfig.LeaseSeconds)), Math.Max(1, NerisConfig.BatchSize), DateTime.UtcNow, cancellationToken))?.ToList() ?? new List<RmsSubmission>();
			result.Claimed = claimed.Count;

			foreach (var submission in claimed)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var processed = await ProcessAsync(submission, cancellationToken);
					switch ((RmsSubmissionState)processed.State)
					{
						case RmsSubmissionState.Accepted: result.Accepted++; break;
						case RmsSubmissionState.Rejected: result.Rejected++; break;
						case RmsSubmissionState.Failed: result.Failed++; break;
						case RmsSubmissionState.AwaitingDestination: result.Delivered++; break;
						default: result.Deferred++; break;
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					result.Errors++;
					Logging.LogException(ex, $"Submission {submission.RmsSubmissionId} could not be processed.");
				}
			}

			result.Message = $"claimed {result.Claimed}, delivered {result.Delivered}, accepted {result.Accepted}, rejected {result.Rejected}, deferred {result.Deferred}, failed {result.Failed}, errors {result.Errors}";
			return result;
		}

		public async Task<RmsSubmission> ProcessAsync(RmsSubmission submission, CancellationToken cancellationToken = default)
		{
			if (submission == null) throw new ArgumentNullException(nameof(submission));
			var now = DateTime.UtcNow;

			var report = await _reports.GetByIdForDepartmentAsync(submission.DepartmentId, submission.RecordId);
			if (report == null || report.DeletedOn.HasValue)
				return await PersistAsync(submission, report, Fatal("The report no longer exists."), now, cancellationToken);

			// A superseding revision or a void may have arrived while this row waited; never deliver stale content.
			if (submission.State == (int)RmsSubmissionState.Superseded || RmsLifecycle.IsTerminal((RmsRecordState)report.State))
			{
				submission.State = (int)RmsSubmissionState.Superseded;
				submission.CompletedOn = now;
				return await ReleaseAsync(submission, now, cancellationToken);
			}

			var profile = await _profiles.GetProfileAsync(submission.DepartmentId);
			if (!await _profiles.IsSubmissionEnabledAsync(submission.DepartmentId))
			{
				submission.NextAttemptOn = now.AddMinutes(Math.Max(1, NerisConfig.StatusPollMinutes));
				return await ReleaseAsync(submission, now, cancellationToken);
			}

			NerisSubmissionOutcome outcome;
			if (submission.State == (int)RmsSubmissionState.AwaitingDestination)
			{
				var nerisId = submission.ExternalId ?? report.NerisIncidentId;
				outcome = string.IsNullOrWhiteSpace(nerisId)
					? Fatal("The submission is awaiting the destination but carries no incident ID.")
					: await _delivery.CheckStatusAsync(profile, nerisId, cancellationToken);
			}
			else
			{
				submission.Attempts += 1;
				submission.SentOn = now;
				outcome = await _delivery.DeliverAsync(profile, submission, report.NerisIncidentId, cancellationToken);
			}

			return await PersistAsync(submission, report, outcome, now, cancellationToken);
		}

		private async Task<RmsSubmission> PersistAsync(RmsSubmission submission, RmsIncidentReport report, NerisSubmissionOutcome outcome, DateTime now, CancellationToken cancellationToken)
		{
			var outboxIds = new List<long>();
			NotificationItem notification = null;

			await InTransactionAsync(async () =>
			{
				if (outcome.ResponseJson != null)
				{
					submission.ResponseJson = outcome.ResponseJson;
					submission.ResponseChecksum = RecordSnapshotSerializer.Checksum(outcome.ResponseJson);
				}
				submission.ResponseStatusCode = outcome.StatusCode;
				if (!string.IsNullOrWhiteSpace(outcome.ExternalId))
					submission.ExternalId = outcome.ExternalId;
				if (!string.IsNullOrWhiteSpace(outcome.ExternalStatus))
					submission.ExternalStatus = outcome.ExternalStatus;

				WorkflowTriggerEventType? trigger = null;
				RmsRecordState? reportState = null;
				string auditPurpose;

				switch (outcome.Kind)
				{
					case NerisOutcomeKind.Created:
					case NerisOutcomeKind.Updated:
					case NerisOutcomeKind.Pending:
						submission.State = (int)RmsSubmissionState.AwaitingDestination;
						submission.NextAttemptOn = now.AddMinutes(Math.Max(1, NerisConfig.StatusPollMinutes));
						submission.ErrorSummary = null;
						reportState = ReportStateAfterDelivery(report);
						auditPurpose = outcome.Kind == NerisOutcomeKind.Pending ? "Submission status pending" : "Submission delivered";
						break;

					case NerisOutcomeKind.Accepted:
						submission.State = (int)RmsSubmissionState.Accepted;
						submission.CompletedOn = now;
						submission.NextAttemptOn = null;
						submission.ErrorSummary = null;
						reportState = RmsRecordState.Accepted;
						trigger = WorkflowTriggerEventType.RecordSubmissionAccepted;
						auditPurpose = "Submission accepted";
						break;

					case NerisOutcomeKind.Rejected:
						submission.State = (int)RmsSubmissionState.Rejected;
						submission.CompletedOn = now;
						submission.NextAttemptOn = null;
						submission.ErrorSummary = Summarize(outcome);
						reportState = RmsRecordState.Rejected;
						trigger = WorkflowTriggerEventType.RecordSubmissionRejected;
						auditPurpose = "Submission rejected";
						break;

					case NerisOutcomeKind.Transient:
						if (submission.Attempts >= Math.Max(1, submission.MaxAttempts))
						{
							submission.State = (int)RmsSubmissionState.Failed;
							submission.CompletedOn = now;
							submission.NextAttemptOn = null;
							submission.ErrorSummary = "Delivery exhausted its retries: " + (outcome.Message ?? "destination unavailable");
							trigger = WorkflowTriggerEventType.RecordSubmissionFailed;
							auditPurpose = "Submission failed (retries exhausted)";
						}
						else
						{
							if (submission.State != (int)RmsSubmissionState.AwaitingDestination)
								submission.State = (int)RmsSubmissionState.Queued;
							submission.NextAttemptOn = now.AddMinutes(Backoff(submission.Attempts));
							submission.ErrorSummary = outcome.Message;
							auditPurpose = "Submission deferred";
						}
						break;

					default:
						submission.State = (int)RmsSubmissionState.Failed;
						submission.CompletedOn = now;
						submission.NextAttemptOn = null;
						submission.ErrorSummary = outcome.Message ?? "Delivery needs operator attention.";
						trigger = WorkflowTriggerEventType.RecordSubmissionFailed;
						auditPurpose = "Submission failed";
						break;
				}

				submission.LeaseOwner = null;
				submission.LeaseExpiresOn = null;
				submission.ModifiedOn = now;
				submission.RowVersion += 1;
				await _submissions.UpdateAsync(submission, cancellationToken, true);

				if (report != null)
				{
					if (!string.IsNullOrWhiteSpace(submission.ExternalId))
						report.NerisIncidentId = submission.ExternalId;
					report.LastSubmissionId = submission.RmsSubmissionId;
					report.LastSubmissionState = submission.State;
					var from = (RmsRecordState)report.State;
					if (reportState.HasValue && reportState.Value != from)
					{
						report.State = (int)reportState.Value;
						if (reportState == RmsRecordState.Accepted) report.AcceptedOn = now;
						if (reportState == RmsRecordState.Rejected) { report.RejectedOn = now; report.RejectionSummary = submission.ErrorSummary; }
					}
					report.ModifiedOn = now;
					report.RowVersion += 1;
					await _reports.UpdateAsync(report, cancellationToken, true);
					await UpdateProjectionStateAsync(report, cancellationToken);

					if (trigger.HasValue)
					{
						var entry = await _outbox.EnqueueAsync(report.DepartmentId, DomainEventProducers.Records, new DomainEventEnvelope
						{
							EventName = trigger.Value.ToString(),
							SchemaVersion = 1,
							AggregateType = IncidentReportsService.IncidentAggregate,
							AggregateId = report.RmsIncidentReportId,
							AggregateVersion = report.RevisionCount,
							Trigger = trigger.Value,
							Payload = new Dictionary<string, object>
							{
								["record"] = IncidentReportsService.RecordBlock(report, null, (RmsRecordState)report.State),
								["record_change"] = new { previous_state = from.ToString(), current_state = ((RmsRecordState)report.State).ToString(), prior_revision_id = (string)null, current_revision_id = report.CurrentRevisionId, reason_code = (string)null },
								["submission"] = IncidentReportsService.SubmissionBlock(submission)
							},
							CorrelationId = report.RmsIncidentReportId,
							OriginClient = RmsOriginClient.System
						}, cancellationToken);
						outboxIds.Add(entry.DomainEventOutboxId);
					}

					if (outcome.Kind == NerisOutcomeKind.Rejected && !string.IsNullOrWhiteSpace(report.AuthorUserId))
						notification = new NotificationItem { DepartmentId = report.DepartmentId, Type = (int)EventTypes.RecordSubmissionRejected, Value = report.RmsIncidentReportId, UserId = report.AuthorUserId };

					await _audits.InsertAsync(new RmsAccessAudit
					{
						DepartmentId = report.DepartmentId,
						RecordId = report.RmsIncidentReportId,
						RevisionId = submission.RevisionId,
						Action = (int)RmsAccessAuditAction.Submit,
						ActorUserId = null,
						Purpose = auditPurpose,
						OriginClient = (int)RmsOriginClient.System,
						Successful = outcome.Kind != NerisOutcomeKind.Fatal,
						OccurredOn = now,
						DetailJson = JsonConvert.SerializeObject(new { submission.RmsSubmissionId, submission.Attempts, outcome.Kind, outcome.StatusCode, submission.ExternalId, submission.ExternalStatus, submission.ResponseChecksum })
					}, cancellationToken, true);
				}
			});

			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);
			if (notification != null)
			{
				try { await _outboundQueue.EnqueueNotification(notification); }
				catch (Exception ex) { Logging.LogException(ex, $"Rejection notification for report {report?.RmsIncidentReportId} could not be queued."); }
			}

			return submission;
		}

		private static RmsRecordState? ReportStateAfterDelivery(RmsIncidentReport report)
		{
			var state = (RmsRecordState)report.State;
			return state == RmsRecordState.Finalized || state == RmsRecordState.Amended || state == RmsRecordState.Corrected || state == RmsRecordState.Rejected
				? RmsRecordState.Submitted
				: (RmsRecordState?)null;
		}

		/// <summary>Exponential backoff in minutes: base, 2x, 4x ... capped at one day.</summary>
		public static int Backoff(int attempts)
		{
			var minutes = Math.Max(1, NerisConfig.RetryBackoffMinutes) * Math.Pow(2, Math.Max(0, attempts - 1));
			return (int)Math.Min(minutes, 24 * 60);
		}

		/// <summary>Codes and field paths only: what workflows, notifications and the queue may see (plan 5.6).</summary>
		public static string Summarize(NerisSubmissionOutcome outcome)
		{
			if (outcome.Errors == null || outcome.Errors.Count == 0)
				return outcome.Message ?? "Rejected by the destination.";
			return string.Join("; ", outcome.Errors.Take(20).Select(e => string.IsNullOrWhiteSpace(e.FieldPath) ? e.Code : $"{e.FieldPath} ({e.Code})"));
		}

		private async Task<RmsSubmission> ReleaseAsync(RmsSubmission submission, DateTime now, CancellationToken cancellationToken)
		{
			submission.LeaseOwner = null;
			submission.LeaseExpiresOn = null;
			submission.ModifiedOn = now;
			submission.RowVersion += 1;
			await _submissions.UpdateAsync(submission, cancellationToken, true);
			return submission;
		}

		private async Task UpdateProjectionStateAsync(RmsIncidentReport report, CancellationToken cancellationToken)
		{
			var projection = await _projections.GetByRecordIdAsync(report.DepartmentId, report.RmsIncidentReportId);
			if (projection == null)
				return;
			projection.State = report.State;
			projection.SearchText = string.Join(" ", new[] { report.RecordNumber, report.DraftReference, report.DisplaySummary, report.IncidentNumber, report.NerisIncidentId }.Where(s => !string.IsNullOrWhiteSpace(s)));
			projection.ModifiedOn = DateTime.UtcNow;
			projection.RowVersion += 1;
			await _projections.UpdateAsync(projection, cancellationToken, true);
		}

		private static NerisSubmissionOutcome Fatal(string message) => new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Fatal, Message = message };

		private async Task InTransactionAsync(Func<Task> work)
		{
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await work();
				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}
		}
	}
}
