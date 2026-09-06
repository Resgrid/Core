using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
		private readonly IRmsSubmissionExchangesRepository _exchanges;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRmsIncidentReportsRepository _reports;
		private readonly IRmsIncidentAnalysesRepository _analyses;
		private readonly IRmsDepartmentCutoversRepository _cutovers;
		private readonly IIncidentAnalysisService _analysisService;
		private readonly IRmsRecordSearchProjectionsRepository _projections;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly INerisProfileService _profiles;
		private readonly INerisSubmissionService _delivery;
		private readonly IDomainEventOutboxService _outbox;
		private readonly IOutboundQueueProvider _outboundQueue;
		private readonly IUnitOfWork _unitOfWork;

		public RecordsSubmissionService(IRmsSubmissionsRepository submissions, IRmsIncidentReportsRepository reports, IRmsIncidentAnalysesRepository analyses, IRmsRecordSearchProjectionsRepository projections,
			IRmsAccessAuditsRepository audits, INerisProfileService profiles, INerisSubmissionService delivery, IDomainEventOutboxService outbox,
			IOutboundQueueProvider outboundQueue, IUnitOfWork unitOfWork, IRmsDepartmentCutoversRepository cutovers, IIncidentAnalysisService analysisService,
			IRmsSubmissionExchangesRepository exchanges, IRecordsAuthorizationService authorization)
		{
			_submissions = submissions;
			_exchanges = exchanges;
			_authorization = authorization;
			_reports = reports;
			_analyses = analyses;
			_cutovers = cutovers;
			_analysisService = analysisService;
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

			// An analysis finalized before its incident was filed has no submission row yet. Queue those first so
			// they join this sweep rather than waiting a whole cycle after the incident finally lands.
			foreach (var cutover in (await _cutovers.GetActiveAsync())?.ToList() ?? new List<RmsDepartmentCutover>())
			{
				try
				{
					await _analysisService.QueueAwaitingIncidentAsync(cutover.DepartmentId, cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Awaiting incident analyses could not be queued for department {cutover.DepartmentId}.");
					result.Errors++;
				}
			}

			var owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
			for (var i = 0; i < Math.Max(1, NerisConfig.BatchSize); i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				// Claim when ready to work; a serial batch must not spend the last item's lease waiting on its predecessors.
				var submission = (await _submissions.ClaimDueBatchAsync(owner, TimeSpan.FromSeconds(Math.Max(30, NerisConfig.LeaseSeconds)), 1, DateTime.UtcNow, cancellationToken))?.FirstOrDefault();
				if (submission == null) break;
				result.Claimed++;
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

		public async Task<List<RmsSubmissionExchange>> GetHistoryAsync(int departmentId, string userId, string submissionId, CancellationToken cancellationToken = default)
		{
			var submission = await _submissions.GetByIdForDepartmentAsync(departmentId, submissionId);
			if (submission == null) throw new InvalidOperationException("The submission does not exist.");
			var analysis = submission.Destination == RmsSubmissionDestinations.NerisIncidentAnalysis ? await _analyses.GetByIdForDepartmentAsync(departmentId, submission.RecordId) : null;
			async Task Authorize()
			{
				if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.SubmitRecords)
					|| !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords)
					|| !await _authorization.CanUserViewRecordAsync(userId, analysis?.IncidentReportId ?? submission.RecordId, departmentId)) throw new UnauthorizedAccessException();
			}
			await Authorize();
			var history = (await _exchanges.GetForSubmissionAsync(departmentId, submissionId))?.ToList() ?? new List<RmsSubmissionExchange>();
			if (history.Any(e => e.DepartmentId != departmentId || e.SubmissionId != submissionId || e.OutcomeJson != null && e.OutcomeChecksum != RecordSnapshotSerializer.Checksum(e.OutcomeJson)))
				throw new InvalidOperationException("The submission exchange history failed its integrity check.");
			await InTransactionAsync(async () => await _audits.InsertAsync(new RmsAccessAudit
			{
				DepartmentId = departmentId, RecordId = submission.RecordId, RevisionId = submission.RevisionId, ActorUserId = userId,
				Action = (int)RmsAccessAuditAction.Export, Purpose = "Submission exchange history", OriginClient = (int)RmsOriginClient.Web, Successful = true, OccurredOn = DateTime.UtcNow
			}, cancellationToken, true));
			await Authorize();
			return history;
		}

		public async Task ReconcileAsync(int departmentId, string userId, string submissionId, long expectedVersion, string externalId, string reason, CancellationToken cancellationToken = default)
		{
			if (externalId?.Length > 100 || string.IsNullOrWhiteSpace(reason) || reason.Length > 2000)
				throw new ArgumentException("A reconciliation reason and a valid destination identifier are required.");
			var submission = await _submissions.GetByIdForDepartmentAsync(departmentId, submissionId);
			if (submission == null) throw new InvalidOperationException("The submission does not exist.");
			var analysis = submission.Destination == RmsSubmissionDestinations.NerisIncidentAnalysis
				? await _analyses.GetByIdForDepartmentAsync(departmentId, submission.RecordId) : null;
			var reportId = analysis?.IncidentReportId ?? submission.RecordId;
			async Task Authorize()
			{
				if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.SubmitRecords)
					|| !await _authorization.CanUserViewRecordAsync(userId, reportId, departmentId)) throw new UnauthorizedAccessException();
			}
			await Authorize();
			if (submission.RowVersion != expectedVersion) throw new RecordConcurrencyException(submission.RecordId, expectedVersion, submission.RowVersion);
			var bindUnsent = submission.DestinationIdentity == null && submission.SentOn == null && submission.Attempts == 0
				&& submission.ExternalId == null && !submission.RequiresReconciliation && !submission.CreatePendingReceipt;
			if (!bindUnsent && !submission.RequiresReconciliation && !submission.CreatePendingReceipt) throw new InvalidOperationException("This submission does not need reconciliation.");
			if (submission.PayloadChecksum != RecordSnapshotSerializer.Checksum(submission.PayloadJson)) throw new InvalidOperationException("The queued payload failed its integrity check.");
			var profile = await _profiles.GetProfileAsync(departmentId);
			var destination = _profiles.GetDestinationIdentity(profile);
			var report = await _reports.GetByIdForDepartmentAsync(departmentId, reportId);
			if (profile == null || report == null || report.ReportingEntityId != profile.NerisEntityId ||
				(!string.IsNullOrEmpty(submission.DestinationIdentity) && submission.DestinationIdentity != destination))
				throw new InvalidOperationException("Restore the original reporting entity and destination profile before reconciliation.");
			if (bindUnsent)
			{
				if (!string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("Use receipt verification for an existing destination filing.");
				if (report.DeletedOn.HasValue || RmsLifecycle.IsTerminal((RmsRecordState)report.State)
					|| (analysis?.CurrentRevisionId ?? report.CurrentRevisionId) != submission.RevisionId)
					throw new InvalidOperationException("Only the current, active revision can be bound for delivery.");
				await InTransactionAsync(async () =>
				{
					await Authorize();
					if (!await _submissions.TryBindUnsentAsync(departmentId, submissionId, expectedVersion, destination, DateTime.UtcNow, cancellationToken))
						throw new RecordConcurrencyException(submission.RecordId, expectedVersion, expectedVersion + 1);
					await _audits.InsertAsync(new RmsAccessAudit { DepartmentId = departmentId, RecordId = submission.RecordId, RevisionId = submission.RevisionId,
						ActorUserId = userId, Action = (int)RmsAccessAuditAction.Submit, Purpose = "Bind unsent legacy submission", OriginClient = (int)RmsOriginClient.Web,
						Successful = true, OccurredOn = DateTime.UtcNow, DetailJson = JsonConvert.SerializeObject(new { submissionId, reason = reason.Trim(), destination }) }, cancellationToken, true);
				});
				return;
			}
			if (string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("A destination identifier is required to verify a filing that may already exist.");
			var outcome = analysis == null ? await _delivery.CheckStatusAsync(profile, externalId.Trim(), cancellationToken)
				: await _delivery.CheckAnalysisStatusAsync(profile, externalId.Trim(), cancellationToken);
			JObject receipt = null;
			try { receipt = string.IsNullOrEmpty(outcome?.ResponseJson) ? null : JObject.Parse(outcome.ResponseJson); } catch (JsonException) { }
			var payload = JObject.Parse(submission.PayloadJson);
			var matchField = analysis == null ? "incident_number" : "neris_id_incident";
			if (outcome?.StatusCode != 200 || (string)receipt?["neris_id"] != externalId.Trim()
				|| string.IsNullOrEmpty((string)payload["base"]?[matchField]) || !JToken.DeepEquals(receipt?["base"]?[matchField], payload["base"]?[matchField])
				|| !JToken.DeepEquals(receipt?["base"]?["incident_number"], payload["base"]?["incident_number"])
				|| analysis == null && ((string)receipt?["base"]?["department_neris_id"] != profile.NerisEntityId
					|| !SameInstant(receipt?["dispatch"]?["call_create"], payload["dispatch"]?["call_create"])))
				throw new InvalidOperationException("The destination receipt does not match the queued report. No filing was changed.");
			await InTransactionAsync(async () =>
			{
				await Authorize();
				if (!await _submissions.TryReconcileReceiptAsync(departmentId, submissionId, expectedVersion, externalId.Trim(), destination, DateTime.UtcNow, cancellationToken))
					throw new RecordConcurrencyException(submission.RecordId, expectedVersion, expectedVersion + 1);
				var history = (await _exchanges.GetForSubmissionAsync(departmentId, submissionId))?.ToList() ?? new List<RmsSubmissionExchange>();
				foreach (var started in history.Where(e => e.Stage == "Started" && !history.Any(a => a.ExchangeId == e.ExchangeId && (a.Stage == "Applied" || a.Stage == "Reconciled"))))
					await AppendExchangeAsync(started, "Reconciled", outcome, cancellationToken);
				await AppendExchangeAsync(new RmsSubmissionExchange { DepartmentId = departmentId, SubmissionId = submissionId, RecordId = submission.RecordId,
					RevisionId = submission.RevisionId, ExchangeId = Guid.NewGuid().ToString(), Operation = "Reconcile", DestinationIdentity = destination,
					PayloadChecksum = submission.PayloadChecksum, AttemptNumber = submission.Attempts }, "Reconciled", outcome, cancellationToken);
				await _audits.InsertAsync(new RmsAccessAudit { DepartmentId = departmentId, RecordId = submission.RecordId, RevisionId = submission.RevisionId,
					ActorUserId = userId, Action = (int)RmsAccessAuditAction.Submit, Purpose = "Reconcile destination receipt", OriginClient = (int)RmsOriginClient.Web,
					Successful = true, OccurredOn = DateTime.UtcNow, DetailJson = JsonConvert.SerializeObject(new { submissionId, externalId = externalId.Trim(), reason = reason.Trim(), destination }) }, cancellationToken, true);
			});
		}

		public async Task ConfirmNotCreatedAsync(int departmentId, string userId, string submissionId, long expectedVersion, string verificationReference, string reason, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(verificationReference) || verificationReference.Length > 500 || string.IsNullOrWhiteSpace(reason) || reason.Length > 2000)
				throw new ArgumentException("Record the destination verification reference and the reason it confirms no filing was created.");
			var submission = Copy(await _submissions.GetByIdForDepartmentAsync(departmentId, submissionId));
			if (submission == null) throw new InvalidOperationException("The submission does not exist.");
			var isAnalysis = submission.Destination == RmsSubmissionDestinations.NerisIncidentAnalysis;
			if (!isAnalysis && submission.Destination != RmsSubmissionDestinations.Neris) throw new InvalidOperationException("Unsupported destination.");
			var destination = _profiles.GetDestinationIdentity(await _profiles.GetProfileAsync(departmentId));
			async Task ValidateAsync()
			{
				if (!await _authorization.IsDepartmentAdminAsync(userId, departmentId) || !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.SubmitRecords)) throw new UnauthorizedAccessException();
				var analysis = isAnalysis ? await _analyses.GetByIdForDepartmentAsync(departmentId, submission.RecordId) : null;
				if (isAnalysis && (analysis == null || analysis.DeletedOn.HasValue || analysis.State == (int)RmsIncidentAnalysisState.Voided)) throw new InvalidOperationException("The analysis is unavailable.");
				var report = await _reports.GetByIdForDepartmentAsync(departmentId, analysis?.IncidentReportId ?? submission.RecordId);
				if (report == null || report.DeletedOn.HasValue || report.PurgedOn.HasValue || RmsLifecycle.IsTerminal((RmsRecordState)report.State)) throw new InvalidOperationException("The report is unavailable.");
				if (!await _authorization.CanUserViewRecordAsync(userId, report.RmsIncidentReportId, departmentId)) throw new UnauthorizedAccessException();
				var profile = await _profiles.GetProfileAsync(departmentId);
				if (string.IsNullOrWhiteSpace(destination) || profile == null || profile.NerisEntityId != report.ReportingEntityId || destination != _profiles.GetDestinationIdentity(profile)
					|| !string.IsNullOrWhiteSpace(submission.DestinationIdentity) && submission.DestinationIdentity != destination)
					throw new InvalidOperationException("Restore the original reporting entity and destination profile before recording the verification.");
				var prior = (await _submissions.GetForRecordAsync(departmentId, submission.RecordId)) ?? Enumerable.Empty<RmsSubmission>();
				if (!string.IsNullOrWhiteSpace(isAnalysis ? analysis.NerisAnalysisId : report.NerisIncidentId) || prior.Any(s => !string.IsNullOrWhiteSpace(s.ExternalId)))
					throw new InvalidOperationException("A destination filing is already recorded. Verify its receipt instead of declaring it absent.");
			}
			await ValidateAsync();
			if (submission.RowVersion != expectedVersion) throw new RecordConcurrencyException(submission.RecordId, expectedVersion, submission.RowVersion);
			if (submission.PayloadChecksum != RecordSnapshotSerializer.Checksum(submission.PayloadJson)) throw new InvalidOperationException("The queued payload failed its integrity check.");
			if (!submission.RequiresReconciliation && !submission.CreatePendingReceipt && submission.State != (int)RmsSubmissionState.Failed && submission.State != (int)RmsSubmissionState.Rejected)
				throw new InvalidOperationException("Only an ambiguous or failed delivery can be resolved as not created.");
			await InTransactionAsync(async () =>
			{
				// Department lock and CAS exclude a worker receipt, requeue or concurrent recovery decision.
				if (!await _submissions.TryConfirmNotCreatedAsync(departmentId, submissionId, expectedVersion, destination, DateTime.UtcNow, cancellationToken))
					throw new RecordConcurrencyException(submission.RecordId, expectedVersion, expectedVersion + 1);
				await ValidateAsync();
				var outcome = new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Rejected,
					Message = "Administrator recorded external verification that no filing was created; a deliberate submission action is required.",
					ResponseJson = JsonConvert.SerializeObject(new { administrator = userId, verificationReference = verificationReference.Trim(), reason = reason.Trim() }) };
				var history = (await _exchanges.GetForSubmissionAsync(departmentId, submissionId))?.ToList() ?? new List<RmsSubmissionExchange>();
				foreach (var started in history.Where(e => e.Stage == "Started" && !history.Any(a => a.ExchangeId == e.ExchangeId && (a.Stage == "Applied" || a.Stage == "Reconciled"))))
					await AppendExchangeAsync(started, "Reconciled", outcome, cancellationToken);
				await AppendExchangeAsync(new RmsSubmissionExchange { DepartmentId = departmentId, SubmissionId = submissionId, RecordId = submission.RecordId,
					RevisionId = submission.RevisionId, ExchangeId = Guid.NewGuid().ToString(), Operation = "ConfirmNotCreated", DestinationIdentity = destination,
					PayloadChecksum = submission.PayloadChecksum, AttemptNumber = submission.Attempts }, "Reconciled", outcome, cancellationToken);
				await _audits.InsertAsync(new RmsAccessAudit { DepartmentId = departmentId, RecordId = submission.RecordId, RevisionId = submission.RevisionId,
					ActorUserId = userId, Action = (int)RmsAccessAuditAction.Submit, Purpose = "Destination absence externally verified", OriginClient = (int)RmsOriginClient.Web,
					Successful = true, OccurredOn = DateTime.UtcNow, DetailJson = JsonConvert.SerializeObject(new { submissionId, verificationReference = verificationReference.Trim(), reason = reason.Trim(), destination }) }, cancellationToken, true);
			});
		}

		private static bool SameInstant(JToken left, JToken right)
		{
			DateTimeOffset? Read(JToken token)
			{
				if (token == null || token.Type == JTokenType.Null) return null;
				if (token.Type == JTokenType.Date) return token.ToObject<DateTimeOffset>();
				var value = token.Value<string>();
				if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var seconds))
				{
					try { return DateTimeOffset.UnixEpoch.AddSeconds((double)seconds); } catch (ArgumentOutOfRangeException) { return null; }
				}
				return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant) ? instant : null;
			}
			var a = Read(left); var b = Read(right);
			return a.HasValue && b.HasValue && a.Value == b.Value;
		}

		public async Task<RmsSubmission> ProcessAsync(RmsSubmission submission, CancellationToken cancellationToken = default)
		{
			if (submission == null) throw new ArgumentNullException(nameof(submission));
			submission = Copy(submission);
			var now = DateTime.UtcNow;
			var current = await _submissions.GetByIdForDepartmentAsync(submission.DepartmentId, submission.RmsSubmissionId);
			if (current == null || current.RowVersion != submission.RowVersion || current.LeaseOwner != submission.LeaseOwner
				|| string.IsNullOrEmpty(submission.LeaseOwner) || current.LeaseExpiresOn <= now || !current.LeaseExpiresOn.HasValue)
				return current ?? submission;

			// The incident-analysis filing (RMS-3) rides the same queue and the same lease, but it is a different
			// endpoint against a different aggregate, and it raises none of triggers 108-111: those describe the
			// incident's own filing, and an analysis outcome must never be mistaken for one.
			if (string.Equals(submission.Destination, RmsSubmissionDestinations.NerisIncidentAnalysis, StringComparison.Ordinal))
				return await ProcessAnalysisAsync(submission, now, cancellationToken);

			var report = Copy(await _reports.GetByIdForDepartmentAsync(submission.DepartmentId, submission.RecordId));
			if (report == null || report.DeletedOn.HasValue || report.PurgedOn.HasValue)
				return await PersistAsync(submission, report, Fatal("The report no longer exists."), now, false, cancellationToken);

			// A superseding revision or a void may have arrived while this row waited; never deliver stale content.
			if (submission.State == (int)RmsSubmissionState.Superseded || RmsLifecycle.IsTerminal((RmsRecordState)report.State) || report.CurrentRevisionId != submission.RevisionId)
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

			var wasDelivery = submission.State != (int)RmsSubmissionState.AwaitingDestination;
			string externalId;
			try { externalId = ResolveDestinationId(await _submissions.GetForRecordAsync(submission.DepartmentId, submission.RecordId), submission.DestinationIdentity, submission.ExternalId ?? report.NerisIncidentId); }
			catch (InvalidOperationException ex) { return await PersistAsync(submission, report, Fatal(ex.Message), now, false, cancellationToken); }
			var operation = !wasDelivery ? "Poll" : string.IsNullOrEmpty(externalId) ? "Create" : "Update";
			try
			{
				var exchange = await ExchangeAsync(submission, profile, operation, () => !wasDelivery
					? _delivery.CheckStatusAsync(profile, externalId, cancellationToken)
					: _delivery.DeliverAsync(profile, submission, externalId, cancellationToken), cancellationToken);
				return await PersistAsync(submission, report, exchange.outcome, DateTime.UtcNow, wasDelivery, cancellationToken, exchange.entry);
			}
			catch (SubmissionLeaseLostException) { return await _submissions.GetByIdForDepartmentAsync(submission.DepartmentId, submission.RmsSubmissionId) ?? submission; }
		}

		/// <summary>
		/// One delivery attempt for an incident-analysis filing. The analysis can only be filed against an incident
		/// the destination already holds, so a missing incident id is a wait — the row is deferred, never failed.
		/// </summary>
		private async Task<RmsSubmission> ProcessAnalysisAsync(RmsSubmission submission, DateTime now, CancellationToken cancellationToken)
		{
			var analysis = Copy(await _analyses.GetByIdForDepartmentAsync(submission.DepartmentId, submission.RecordId));
			if (analysis == null || analysis.DeletedOn.HasValue)
				return await PersistAnalysisAsync(submission, null, Fatal("The incident analysis no longer exists."), now, false, cancellationToken);

			if (submission.State == (int)RmsSubmissionState.Superseded || (RmsIncidentAnalysisState)analysis.State == RmsIncidentAnalysisState.Voided || analysis.CurrentRevisionId != submission.RevisionId)
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

			var report = await _reports.GetByIdForDepartmentAsync(submission.DepartmentId, analysis.IncidentReportId);

			var wasDelivery = submission.State != (int)RmsSubmissionState.AwaitingDestination;
			string externalId;
			string parentExternalId;
			try
			{
				externalId = ResolveDestinationId(await _submissions.GetForRecordAsync(submission.DepartmentId, submission.RecordId), submission.DestinationIdentity, submission.ExternalId ?? analysis.NerisAnalysisId);
				parentExternalId = ResolveDestinationId(await _submissions.GetForRecordAsync(submission.DepartmentId, analysis.IncidentReportId), submission.DestinationIdentity, report?.NerisIncidentId);
			}
			catch (InvalidOperationException ex) { return await PersistAnalysisAsync(submission, analysis, Fatal(ex.Message), now, false, cancellationToken); }
			var operation = !wasDelivery ? "Poll" : string.IsNullOrEmpty(externalId) ? "Create" : "Update";
			try
			{
				var exchange = await ExchangeAsync(submission, profile, operation, () => !wasDelivery
					? _delivery.CheckAnalysisStatusAsync(profile, externalId, cancellationToken)
					: _delivery.DeliverAnalysisAsync(profile, submission, parentExternalId, externalId, cancellationToken), cancellationToken);
				return await PersistAnalysisAsync(submission, analysis, exchange.outcome, DateTime.UtcNow, wasDelivery, cancellationToken, exchange.entry);
			}
			catch (SubmissionLeaseLostException) { return await _submissions.GetByIdForDepartmentAsync(submission.DepartmentId, submission.RmsSubmissionId) ?? submission; }
		}

		private async Task<RmsSubmission> PersistAnalysisAsync(RmsSubmission submission, RmsIncidentAnalysis analysis, NerisSubmissionOutcome outcome, DateTime now, bool wasDelivery, CancellationToken cancellationToken, RmsSubmissionExchange exchange = null)
		{
			await InTransactionAsync(async () =>
			{
				await FenceAsync(submission, cancellationToken);
				analysis = await CurrentAnalysisAsync(submission, cancellationToken);
				if (outcome.DeliveryUncertain || submission.CreatePendingReceipt && outcome.Kind == NerisOutcomeKind.Fatal && exchange?.Stage != "Response") { outcome.Kind = NerisOutcomeKind.Fatal; submission.RequiresReconciliation = true; }
                else if (exchange?.Stage == "Response") { submission.RequiresReconciliation = false; submission.CreatePendingReceipt = false; }
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

				RmsIncidentAnalysisState? analysisState = null;
				string auditPurpose;

				switch (outcome.Kind)
				{
					case NerisOutcomeKind.Created:
					case NerisOutcomeKind.Updated:
					case NerisOutcomeKind.Pending:
						submission.State = (int)RmsSubmissionState.AwaitingDestination;
						submission.NextAttemptOn = now.AddMinutes(Math.Max(1, NerisConfig.StatusPollMinutes));
						submission.ErrorSummary = null;
						analysisState = RmsIncidentAnalysisState.Submitted;
						auditPurpose = "Analysis delivered";
						break;

					case NerisOutcomeKind.Accepted:
						submission.State = (int)RmsSubmissionState.Accepted;
						submission.CompletedOn = now;
						submission.NextAttemptOn = null;
						submission.ErrorSummary = null;
						analysisState = RmsIncidentAnalysisState.Accepted;
						auditPurpose = "Analysis accepted";
						break;

					case NerisOutcomeKind.Rejected:
						submission.State = (int)RmsSubmissionState.Rejected;
						submission.CompletedOn = now;
						submission.NextAttemptOn = null;
						submission.ErrorSummary = Summarize(outcome);
						analysisState = RmsIncidentAnalysisState.Rejected;
						auditPurpose = "Analysis rejected";
						break;

					case NerisOutcomeKind.Transient:
						// Only a delivery spends the retry budget. A status poll leaves Attempts untouched, so once a
						// row reaches AwaitingDestination on its last allowed attempt the first transient poll error
						// would otherwise fail a submission the destination already holds and may still accept.
						if (wasDelivery && submission.Attempts >= Math.Max(1, submission.MaxAttempts))
						{
							submission.State = (int)RmsSubmissionState.Failed;
							submission.CompletedOn = now;
							submission.NextAttemptOn = null;
							submission.ErrorSummary = "Delivery exhausted its retries: " + (outcome.Message ?? "destination unavailable");
							auditPurpose = "Analysis submission failed (retries exhausted)";
						}
						else
						{
							if (submission.State != (int)RmsSubmissionState.AwaitingDestination)
								submission.State = (int)RmsSubmissionState.Queued;
							submission.NextAttemptOn = now.AddMinutes(Backoff(submission.Attempts));
							submission.ErrorSummary = outcome.Message;
							auditPurpose = "Analysis submission deferred";
						}
						break;

					default:
						submission.State = (int)RmsSubmissionState.Failed;
						submission.CompletedOn = now;
						submission.NextAttemptOn = null;
						submission.ErrorSummary = outcome.Message ?? "Delivery needs operator attention.";
						auditPurpose = "Analysis submission failed";
						break;
				}

				submission.LeaseOwner = null;
				submission.LeaseExpiresOn = null;
				submission.ModifiedOn = now;
				submission.RowVersion += 1;
				await _submissions.UpdateAsync(submission, cancellationToken, true);
				if (exchange?.Stage == "Response") await AppendExchangeAsync(exchange, "Applied", null, cancellationToken);

				if (analysis != null)
				{
					if (!string.IsNullOrWhiteSpace(submission.ExternalId))
						analysis.NerisAnalysisId = submission.ExternalId;
					analysis.LastSubmissionId = submission.RmsSubmissionId;
					analysis.LastSubmissionState = submission.State;
					if (analysisState.HasValue)
					{
						analysis.State = (int)analysisState.Value;
						if (analysisState == RmsIncidentAnalysisState.Accepted) analysis.AcceptedOn = now;
						if (analysisState == RmsIncidentAnalysisState.Rejected) { analysis.RejectedOn = now; analysis.RejectionSummary = submission.ErrorSummary; }
					}
					analysis.ModifiedOn = now;
					analysis.RowVersion += 1;
					await _analyses.UpdateAsync(analysis, cancellationToken, true);

					await _audits.InsertAsync(new RmsAccessAudit
					{
						DepartmentId = analysis.DepartmentId,
						RecordId = analysis.RmsIncidentAnalysisId,
						RevisionId = submission.RevisionId,
						Action = (int)RmsAccessAuditAction.Submit,
						Purpose = auditPurpose,
						OriginClient = (int)RmsOriginClient.System,
						Successful = outcome.Kind != NerisOutcomeKind.Fatal,
						OccurredOn = now,
						DetailJson = JsonConvert.SerializeObject(new { submission.RmsSubmissionId, submission.Attempts, outcome.Kind, outcome.StatusCode, submission.ExternalId, submission.ExternalStatus, submission.ResponseChecksum })
					}, cancellationToken, true);
				}
			});

			return submission;
		}

		private async Task<RmsSubmission> PersistAsync(RmsSubmission submission, RmsIncidentReport report, NerisSubmissionOutcome outcome, DateTime now, bool wasDelivery, CancellationToken cancellationToken, RmsSubmissionExchange exchange = null)
		{
			var outboxIds = new List<long>();
			NotificationItem notification = null;

			await InTransactionAsync(async () =>
			{
				await FenceAsync(submission, cancellationToken);
				report = await CurrentReportAsync(submission, cancellationToken);
				if (outcome.DeliveryUncertain || submission.CreatePendingReceipt && outcome.Kind == NerisOutcomeKind.Fatal && exchange?.Stage != "Response") { outcome.Kind = NerisOutcomeKind.Fatal; submission.RequiresReconciliation = true; }
                else if (exchange?.Stage == "Response") { submission.RequiresReconciliation = false; submission.CreatePendingReceipt = false; }
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
						// Only a delivery spends the retry budget. A status poll leaves Attempts untouched, so once a
						// row reaches AwaitingDestination on its last allowed attempt the first transient poll error
						// would otherwise fail a submission the destination already holds and may still accept.
						if (wasDelivery && submission.Attempts >= Math.Max(1, submission.MaxAttempts))
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
				if (exchange?.Stage == "Response") await AppendExchangeAsync(exchange, "Applied", null, cancellationToken);

				if (report != null)
				{
					if (!string.IsNullOrWhiteSpace(submission.ExternalId))
						report.NerisIncidentId = submission.ExternalId;
					report.LastSubmissionId = submission.RmsSubmissionId;
					report.LastSubmissionState = submission.State;
					var from = (RmsRecordState)report.State;
					if (reportState.HasValue && reportState.Value != from && string.IsNullOrEmpty(report.AmendsRevisionId))
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
			if (report == null || !string.IsNullOrEmpty(report.AmendsRevisionId)) return null;
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
			try
			{
				await InTransactionAsync(async () =>
				{
					await FenceAsync(submission, cancellationToken);
					submission.LeaseOwner = null;
					submission.LeaseExpiresOn = null;
					submission.ModifiedOn = now;
					await _submissions.UpdateAsync(submission, cancellationToken, true);
				});
			}
			catch (SubmissionLeaseLostException) { return await _submissions.GetByIdForDepartmentAsync(submission.DepartmentId, submission.RmsSubmissionId) ?? submission; }
			return submission;
		}

		private sealed class SubmissionLeaseLostException : Exception { }
		internal static void RequireResolvedCreates(IEnumerable<RmsSubmission> submissions)
		{
			if ((submissions ?? Enumerable.Empty<RmsSubmission>()).Any(s => s.RequiresReconciliation || s.CreatePendingReceipt))
				throw new InvalidOperationException("An earlier delivery may have created this report. Resolve that delivery before queuing another revision.");
		}

		internal static string ResolveDestinationId(IEnumerable<RmsSubmission> submissions, string destination, string declaredId)
		{
			var receipts = (submissions ?? Enumerable.Empty<RmsSubmission>()).Where(s => !string.IsNullOrWhiteSpace(s.ExternalId)).ToList();
			if (receipts.Any(s => s.DestinationIdentity != destination))
				throw new InvalidOperationException("This report has a filing in another destination. Restore that profile or explicitly reconcile the destination before submitting.");
			var ids = receipts.Select(s => s.ExternalId).Distinct(StringComparer.Ordinal).ToList();
			if (ids.Count > 1 || !string.IsNullOrEmpty(declaredId) && !ids.Contains(declaredId, StringComparer.Ordinal))
				throw new InvalidOperationException("The report's destination identifier is not bound to a verified filing. Reconcile it before submitting.");
			return ids.FirstOrDefault();
		}
		private static T Copy<T>(T value) where T : class => value == null ? null : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));

		private async Task FenceAsync(RmsSubmission submission, CancellationToken cancellationToken)
		{
			if (!await _submissions.TryFenceLeaseAsync(submission.DepartmentId, submission.RmsSubmissionId, submission.RowVersion, submission.LeaseOwner, DateTime.UtcNow, cancellationToken))
				throw new SubmissionLeaseLostException();
			submission.RowVersion++;
		}

		// Reload after HTTP and take a short optimistic write lock. An edit, void, or newer revision must win over a late response.
		private async Task<RmsIncidentReport> CurrentReportAsync(RmsSubmission submission, CancellationToken cancellationToken)
		{
			var report = Copy(await _reports.GetByIdForDepartmentAsync(submission.DepartmentId, submission.RecordId));
			if (report == null || report.DeletedOn.HasValue || report.PurgedOn.HasValue || RmsLifecycle.IsTerminal((RmsRecordState)report.State)
				|| report.CurrentRevisionId != submission.RevisionId || report.LastSubmissionId != submission.RmsSubmissionId) return null;
			if (!await _reports.TryBumpRowVersionAsync(report.DepartmentId, report.RmsIncidentReportId, report.RowVersion, cancellationToken)) throw new SubmissionLeaseLostException();
			report.RowVersion++;
			return report;
		}

		private async Task<RmsIncidentAnalysis> CurrentAnalysisAsync(RmsSubmission submission, CancellationToken cancellationToken)
		{
			var analysis = Copy(await _analyses.GetByIdForDepartmentAsync(submission.DepartmentId, submission.RecordId));
			if (analysis == null || analysis.DeletedOn.HasValue || analysis.State == (int)RmsIncidentAnalysisState.Voided
				|| analysis.CurrentRevisionId != submission.RevisionId || analysis.LastSubmissionId != submission.RmsSubmissionId) return null;
			if (!await _analyses.TryBumpRowVersionAsync(analysis.DepartmentId, analysis.RmsIncidentAnalysisId, analysis.RowVersion, cancellationToken)) throw new SubmissionLeaseLostException();
			analysis.RowVersion++;
			return analysis;
		}

		private async Task<(NerisSubmissionOutcome outcome, RmsSubmissionExchange entry)> ExchangeAsync(RmsSubmission submission, RmsNerisProfile profile, string operation,
			Func<Task<NerisSubmissionOutcome>> send, CancellationToken cancellationToken)
		{
			if (profile == null || string.IsNullOrWhiteSpace(submission.DestinationIdentity)
				|| submission.DestinationIdentity != _profiles.GetDestinationIdentity(profile))
				return (Fatal("The queued destination does not match the current profile. Restore the pinned profile before retrying."), null);
			if (string.IsNullOrWhiteSpace(submission.PayloadJson) || submission.PayloadChecksum != RecordSnapshotSerializer.Checksum(submission.PayloadJson))
				return (Fatal("The queued payload failed its integrity check."), null);

			var history = (await _exchanges.GetForSubmissionAsync(submission.DepartmentId, submission.RmsSubmissionId))?.ToList() ?? new List<RmsSubmissionExchange>();
			var unfinished = history.Where(e => e.Stage == "Started" && !history.Any(a => a.ExchangeId == e.ExchangeId && (a.Stage == "Applied" || a.Stage == "Reconciled"))).OrderByDescending(e => e.OccurredOn).FirstOrDefault();
			if (unfinished != null)
			{
				var receipt = history.SingleOrDefault(e => e.ExchangeId == unfinished.ExchangeId && e.Stage == "Response");
				if (receipt != null)
				{
					if (receipt.DestinationIdentity != submission.DestinationIdentity || receipt.PayloadChecksum != submission.PayloadChecksum
						|| receipt.OutcomeChecksum != RecordSnapshotSerializer.Checksum(receipt.OutcomeJson)) return (Fatal("The saved response failed its integrity check."), null);
					return (JsonConvert.DeserializeObject<NerisSubmissionOutcome>(receipt.OutcomeJson), receipt);
				}
				// No receipt means a process may have stopped after the remote POST committed. Never issue a second create blindly.
				if (unfinished.Operation == "Create") return (Uncertain(), unfinished);
			}
			if (submission.RequiresReconciliation || submission.CreatePendingReceipt) return (Uncertain(), null);

			var entry = new RmsSubmissionExchange
			{
				DepartmentId = submission.DepartmentId, SubmissionId = submission.RmsSubmissionId, RecordId = submission.RecordId,
				RevisionId = submission.RevisionId, ExchangeId = Guid.NewGuid().ToString(), Operation = operation,
				DestinationIdentity = submission.DestinationIdentity, PayloadChecksum = submission.PayloadChecksum
			};
			await InTransactionAsync(async () =>
			{
				await FenceAsync(submission, cancellationToken);
				if (submission.Destination == RmsSubmissionDestinations.NerisIncidentAnalysis)
				{
					if (await CurrentAnalysisAsync(submission, cancellationToken) == null) throw new SubmissionLeaseLostException();
				}
				else if (await CurrentReportAsync(submission, cancellationToken) == null) throw new SubmissionLeaseLostException();
				if (operation != "Poll") { submission.Attempts++; submission.SentOn ??= DateTime.UtcNow; }
				if (operation == "Create") submission.CreatePendingReceipt = true;
				entry.AttemptNumber = submission.Attempts;
				await _submissions.UpdateAsync(submission, cancellationToken, true);
				await AppendExchangeAsync(entry, "Started", null, cancellationToken);
			});

			NerisSubmissionOutcome outcome;
			try { outcome = await send(); }
			catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				Logging.LogException(ex, "RMS destination exchange failed.");
				outcome = operation == "Create" ? Uncertain() : new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Transient, Message = "Destination exchange failed." };
			}
			outcome ??= operation == "Create" ? Uncertain() : Fatal("The destination returned no outcome.");
			if (operation == "Create" && (outcome.StatusCode >= 200 && outcome.StatusCode < 300 || outcome.Kind == NerisOutcomeKind.Created || outcome.Kind == NerisOutcomeKind.Pending || outcome.Kind == NerisOutcomeKind.Accepted)
				&& string.IsNullOrWhiteSpace(outcome.ExternalId))
			{
				outcome.DeliveryUncertain = true;
				outcome.Message = Uncertain().Message;
			}
			// Commit the receipt independently. If the state transaction fails, the next lease replays this receipt, not HTTP.
			await InTransactionAsync(async () => await AppendExchangeAsync(entry, "Response", outcome, cancellationToken));
			entry.Stage = "Response";
			return (outcome, entry);
		}

		private async Task AppendExchangeAsync(RmsSubmissionExchange source, string stage, NerisSubmissionOutcome outcome, CancellationToken cancellationToken)
		{
			var entry = Copy(source);
			entry.RmsSubmissionExchangeId = Guid.NewGuid().ToString();
			entry.Stage = stage;
			entry.OccurredOn = DateTime.UtcNow;
			entry.OutcomeJson = outcome == null ? null : JsonConvert.SerializeObject(outcome);
			entry.OutcomeChecksum = entry.OutcomeJson == null ? null : RecordSnapshotSerializer.Checksum(entry.OutcomeJson);
			await _exchanges.InsertAsync(entry, cancellationToken, true);
		}

		private static NerisSubmissionOutcome Uncertain() => new NerisSubmissionOutcome
		{
			Kind = NerisOutcomeKind.Fatal, DeliveryUncertain = true,
			Message = "The destination may have created this report. Reconcile its destination identifier before another delivery."
		};

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
