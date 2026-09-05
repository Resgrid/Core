using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Worker 41 logic (RMS plan sections 5.3, 5.5, 5.6): delivery outcomes drive the submission and report states,
	/// triggers 109–111 and notification 33, transient failures back off and fail after MaxAttempts, and stale or
	/// disabled work is never delivered.
	/// </summary>
	[TestFixture]
	public class RecordsSubmissionServiceTests
	{
		private const int Dept = 42;
		private const string ReportId = "report-1";

		private FakeIncidentStore _store;
		private Mock<INerisProfileService> _profiles;
		private Mock<INerisSubmissionService> _delivery;
		private Mock<IOutboundQueueProvider> _outboundQueue;
		private List<NotificationItem> _notifications;
		private RmsNerisProfile _profile;
		private bool _enabled;
		private bool _previousEnabled;
		private RecordsSubmissionService _service;

		[SetUp]
		public void SetUp()
		{
			_previousEnabled = NerisConfig.Enabled;
			NerisConfig.Enabled = true;
			_store = new FakeIncidentStore();

			_profile = new RmsNerisProfile { DepartmentId = Dept, NerisEntityId = "FD24027000", ContractVersion = "1.4.78", IsEnabled = true };
			_enabled = true;
			_profiles = new Mock<INerisProfileService>();
			_profiles.Setup(p => p.GetProfileAsync(Dept)).ReturnsAsync(() => _profile);
			_profiles.Setup(p => p.IsSubmissionEnabledAsync(Dept)).ReturnsAsync(() => _enabled);

			_delivery = new Mock<INerisSubmissionService>();

			_notifications = new List<NotificationItem>();
			_outboundQueue = new Mock<IOutboundQueueProvider>();
			_outboundQueue.Setup(q => q.EnqueueNotification(It.IsAny<NotificationItem>())).Callback<NotificationItem>(n => _notifications.Add(n)).ReturnsAsync(true);

			var outbox = new DomainEventOutboxService(_store.Shared.OutboxRepo.Object, new Mock<IEventAggregator>().Object);
			_service = new RecordsSubmissionService(_store.SubmissionsRepo.Object, _store.ReportsRepo.Object, _store.AnalysesRepo.Object, _store.Shared.ProjectionsRepo.Object,
				_store.Shared.AuditsRepo.Object, _profiles.Object, _delivery.Object, outbox, _outboundQueue.Object, _store.UnitOfWork.Object, _store.Shared.CutoversRepo.Object, Mock.Of<IIncidentAnalysisService>());
		}

		[TearDown]
		public void TearDown()
		{
			NerisConfig.Enabled = _previousEnabled;
		}

		[Test]
		public async Task Created_outcome_moves_the_report_to_Submitted_and_awaits_the_destination()
		{
			var submission = Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued);
			_delivery.Setup(d => d.DeliverAsync(_profile, submission, null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Created, StatusCode = 201, ExternalId = "FD24027000I2026000123", ExternalStatus = "SUBMITTED", ResponseJson = "{\"neris_id\":\"FD24027000I2026000123\"}" });

			var result = await _service.ProcessAsync(submission);

			result.State.Should().Be((int)RmsSubmissionState.AwaitingDestination);
			result.Attempts.Should().Be(1);
			result.SentOn.Should().NotBeNull();
			result.ExternalId.Should().Be("FD24027000I2026000123");
			result.ExternalStatus.Should().Be("SUBMITTED");
			result.ResponseStatusCode.Should().Be(201);
			result.ResponseChecksum.Should().Be(RecordSnapshotSerializer.Checksum(result.ResponseJson), "the response artifact is stored verbatim with its checksum");
			result.NextAttemptOn.Should().NotBeNull("the status poll is scheduled");
			result.LeaseOwner.Should().BeNull();
			result.CompletedOn.Should().BeNull();

			var report = _store.Reports.Single();
			report.State.Should().Be((int)RmsRecordState.Submitted);
			report.NerisIncidentId.Should().Be("FD24027000I2026000123");
			report.LastSubmissionId.Should().Be(submission.RmsSubmissionId);
			report.LastSubmissionState.Should().Be((int)RmsSubmissionState.AwaitingDestination);
			_store.Projections.Single().State.Should().Be((int)RmsRecordState.Submitted);
			_store.Projections.Single().SearchText.Should().Contain("FD24027000I2026000123");
			_store.Outbox.Should().BeEmpty("108 was raised when the submission was queued; delivery alone raises nothing");
			_notifications.Should().BeEmpty();
			_store.Audits.Should().ContainSingle(a => a.Action == (int)RmsAccessAuditAction.Submit && a.Purpose == "Submission delivered" && a.Successful);
		}

		[Test]
		public async Task Accepted_status_raises_109()
		{
			var submission = Seed(RmsRecordState.Submitted, RmsSubmissionState.AwaitingDestination, externalId: "FD24027000I2026000123");
			_delivery.Setup(d => d.CheckStatusAsync(_profile, "FD24027000I2026000123", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Accepted, StatusCode = 200, ExternalId = "FD24027000I2026000123", ExternalStatus = "APPROVED", ResponseJson = "{\"incident_status\":{\"status\":\"APPROVED\"}}" });

			var result = await _service.ProcessAsync(submission);

			result.State.Should().Be((int)RmsSubmissionState.Accepted);
			result.Attempts.Should().Be(0, "a status poll is not a delivery attempt");
			result.CompletedOn.Should().NotBeNull();
			result.NextAttemptOn.Should().BeNull();
			var report = _store.Reports.Single();
			report.State.Should().Be((int)RmsRecordState.Accepted);
			report.AcceptedOn.Should().NotBeNull();

			var entry = _store.Outbox.Single();
			entry.TriggerEventType.Should().Be((int)WorkflowTriggerEventType.RecordSubmissionAccepted);
			entry.AggregateType.Should().Be(IncidentReportsService.IncidentAggregate);
			var payload = JObject.Parse(entry.PayloadJson);
			payload["submission"]["state"].Value<string>().Should().Be("Accepted");
			payload["submission"]["external_status"].Value<string>().Should().Be("APPROVED");
			payload["record"]["neris_incident_id"].Value<string>().Should().Be("FD24027000I2026000123");
			payload["record_change"]["previous_state"].Value<string>().Should().Be("Submitted");
			_notifications.Should().BeEmpty();
			_delivery.Verify(d => d.DeliverAsync(It.IsAny<RmsNerisProfile>(), It.IsAny<RmsSubmission>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Rejected_outcome_raises_110_and_notification_33_with_codes_and_paths_only()
		{
			var submission = Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued);
			_delivery.Setup(d => d.DeliverAsync(_profile, submission, null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new NerisSubmissionOutcome
				{
					Kind = NerisOutcomeKind.Rejected, StatusCode = 422, ExternalStatus = "REJECTED", ResponseJson = "{\"detail\":[{\"loc\":[\"body\",\"dispatch\",\"call_answered\"],\"msg\":\"field required\",\"type\":\"value_error.missing\"}]}",
					Errors = new List<NerisSubmissionError> { new NerisSubmissionError { Code = "value_error.missing", FieldPath = "dispatch.call_answered", Message = "field required" }, new NerisSubmissionError { Code = "invalid_entity" } }
				});

			var result = await _service.ProcessAsync(submission);

			result.State.Should().Be((int)RmsSubmissionState.Rejected);
			result.ErrorSummary.Should().Be("dispatch.call_answered (value_error.missing); invalid_entity");
			result.CompletedOn.Should().NotBeNull();
			var report = _store.Reports.Single();
			report.State.Should().Be((int)RmsRecordState.Rejected);
			report.RejectedOn.Should().NotBeNull();
			report.RejectionSummary.Should().Be(result.ErrorSummary);

			var entry = _store.Outbox.Single();
			entry.TriggerEventType.Should().Be((int)WorkflowTriggerEventType.RecordSubmissionRejected);
			var payload = JObject.Parse(entry.PayloadJson);
			payload["submission"]["error_summary"].Value<string>().Should().Be(result.ErrorSummary);
			entry.PayloadJson.Should().NotContain("field required", "destination response bodies never reach the Workflow payload");

			var notification = _notifications.Single();
			notification.Type.Should().Be((int)EventTypes.RecordSubmissionRejected);
			notification.DepartmentId.Should().Be(Dept);
			notification.Value.Should().Be(ReportId);
			notification.UserId.Should().Be("author");
		}

		[Test]
		public async Task Transient_outcome_backs_off_then_fails_after_MaxAttempts_with_111()
		{
			var submission = Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued, maxAttempts: 2);
			_delivery.Setup(d => d.DeliverAsync(_profile, submission, null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Transient, StatusCode = 503, Message = "NERIS returned 503." });

			var first = await _service.ProcessAsync(submission);
			first.State.Should().Be((int)RmsSubmissionState.Queued);
			first.Attempts.Should().Be(1);
			first.NextAttemptOn.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(RecordsSubmissionService.Backoff(1)), TimeSpan.FromSeconds(10));
			first.ErrorSummary.Should().Be("NERIS returned 503.");
			first.LeaseOwner.Should().BeNull();
			_store.Reports.Single().State.Should().Be((int)RmsRecordState.Finalized, "a deferred delivery leaves the report where it was");
			_store.Outbox.Should().BeEmpty();

			var second = await _service.ProcessAsync(submission);
			second.State.Should().Be((int)RmsSubmissionState.Failed);
			second.Attempts.Should().Be(2);
			second.CompletedOn.Should().NotBeNull();
			second.ErrorSummary.Should().StartWith("Delivery exhausted its retries");
			_store.Outbox.Should().ContainSingle(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordSubmissionFailed);
			_store.Reports.Single().LastSubmissionState.Should().Be((int)RmsSubmissionState.Failed);
			_notifications.Should().BeEmpty("a failed delivery is an operator concern, not the author's");
		}

		[Test]
		public async Task A_transient_status_poll_never_spends_the_delivery_retry_budget()
		{
			// The row was delivered on its last allowed attempt and the destination now holds the revision. Status
			// polls leave Attempts alone, so treating a poll error as an exhausted retry would fail a submission the
			// destination may still accept.
			var submission = Seed(RmsRecordState.Submitted, RmsSubmissionState.AwaitingDestination, maxAttempts: 2, externalId: "FD24027000I2026000123");
			submission.Attempts = 2;
			_delivery.Setup(d => d.CheckStatusAsync(_profile, "FD24027000I2026000123", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Transient, StatusCode = 503, Message = "NERIS returned 503." });

			var result = await _service.ProcessAsync(submission);

			result.State.Should().Be((int)RmsSubmissionState.AwaitingDestination);
			result.Attempts.Should().Be(2, "a status poll is not a delivery attempt");
			result.CompletedOn.Should().BeNull();
			result.NextAttemptOn.Should().NotBeNull();
			result.ErrorSummary.Should().Be("NERIS returned 503.");
			_store.Outbox.Should().NotContain(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordSubmissionFailed);
		}

		[Test]
		public async Task Fatal_outcome_fails_immediately()
		{
			var submission = Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued, maxAttempts: 5);
			_delivery.Setup(d => d.DeliverAsync(_profile, submission, null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Fatal, StatusCode = 401, Message = "The NERIS credential was refused." });

			var result = await _service.ProcessAsync(submission);

			result.State.Should().Be((int)RmsSubmissionState.Failed);
			result.ErrorSummary.Should().Be("The NERIS credential was refused.");
			_store.Outbox.Should().ContainSingle(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordSubmissionFailed);
			_store.Audits.Single().Successful.Should().BeFalse();
		}

		[Test]
		public async Task Voided_report_supersedes_the_submission_without_delivery()
		{
			var submission = Seed(RmsRecordState.Voided, RmsSubmissionState.Queued);

			var result = await _service.ProcessAsync(submission);

			result.State.Should().Be((int)RmsSubmissionState.Superseded);
			result.CompletedOn.Should().NotBeNull();
			result.LeaseOwner.Should().BeNull();
			_delivery.Verify(d => d.DeliverAsync(It.IsAny<RmsNerisProfile>(), It.IsAny<RmsSubmission>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_store.Outbox.Should().BeEmpty();
		}

		[Test]
		public async Task Disabled_destination_defers_without_delivery()
		{
			_enabled = false;
			var submission = Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued);

			var result = await _service.ProcessAsync(submission);

			result.State.Should().Be((int)RmsSubmissionState.Queued);
			result.Attempts.Should().Be(0);
			result.NextAttemptOn.Should().NotBeNull();
			result.LeaseOwner.Should().BeNull();
			_delivery.Verify(d => d.DeliverAsync(It.IsAny<RmsNerisProfile>(), It.IsAny<RmsSubmission>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Missing_report_fails_the_submission()
		{
			var submission = new RmsSubmission { RmsSubmissionId = "orphan", DepartmentId = Dept, RecordId = "gone", State = (int)RmsSubmissionState.Queued, MaxAttempts = 5, QueuedOn = DateTime.UtcNow };
			_store.Submissions.Add(submission);

			var result = await _service.ProcessAsync(submission);

			result.State.Should().Be((int)RmsSubmissionState.Failed);
			result.ErrorSummary.Should().Be("The report no longer exists.");
			_store.Outbox.Should().BeEmpty("there is no aggregate to raise an event for");
		}

		[Test]
		public async Task Sweep_claims_due_rows_and_counts_outcomes()
		{
			var a = Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued, id: "a");
			var b = Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued, id: "b", reportId: "report-2");
			var notDue = Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued, id: "c", reportId: "report-3");
			notDue.NextAttemptOn = DateTime.UtcNow.AddHours(1);
			_delivery.Setup(d => d.DeliverAsync(It.IsAny<RmsNerisProfile>(), It.IsAny<RmsSubmission>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((RmsNerisProfile p, RmsSubmission s, string existing, CancellationToken c) => new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Created, StatusCode = 201, ExternalId = "FD24027000I" + s.RmsSubmissionId, ExternalStatus = "SUBMITTED" });

			var result = await _service.SweepAsync();

			result.Claimed.Should().Be(2);
			result.Delivered.Should().Be(2);
			result.Errors.Should().Be(0);
			a.State.Should().Be((int)RmsSubmissionState.AwaitingDestination);
			b.State.Should().Be((int)RmsSubmissionState.AwaitingDestination);
			notDue.State.Should().Be((int)RmsSubmissionState.Queued);
			result.Message.Should().Contain("claimed 2");
		}

		[Test]
		public async Task Sweep_is_a_no_op_while_NERIS_is_disabled()
		{
			NerisConfig.Enabled = false;
			Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued);

			var result = await _service.SweepAsync();

			result.Claimed.Should().Be(0);
			_store.Submissions.Single().LeaseOwner.Should().BeNull();
		}

		[Test]
		public void Backoff_doubles_from_the_configured_base_and_caps_at_one_day()
		{
			var baseMinutes = Math.Max(1, NerisConfig.RetryBackoffMinutes);

			RecordsSubmissionService.Backoff(1).Should().Be(baseMinutes);
			RecordsSubmissionService.Backoff(2).Should().Be(baseMinutes * 2);
			RecordsSubmissionService.Backoff(3).Should().Be(baseMinutes * 4);
			RecordsSubmissionService.Backoff(40).Should().Be(24 * 60);
		}

		[Test]
		public void Summarize_uses_codes_and_field_paths_never_messages()
		{
			var outcome = new NerisSubmissionOutcome
			{
				Kind = NerisOutcomeKind.Rejected,
				Message = "Unprocessable Entity",
				Errors = new List<NerisSubmissionError> { new NerisSubmissionError { Code = "missing", FieldPath = "base.location", Message = "Location with PII" } }
			};

			RecordsSubmissionService.Summarize(outcome).Should().Be("base.location (missing)");
			RecordsSubmissionService.Summarize(new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Rejected, Message = "Rejected." }).Should().Be("Rejected.");
		}

		private RmsSubmission Seed(RmsRecordState reportState, RmsSubmissionState submissionState, int maxAttempts = 5, string id = "sub-1", string reportId = ReportId, string externalId = null)
		{
			var now = DateTime.UtcNow;
			if (_store.Reports.All(r => r.RmsIncidentReportId != reportId))
			{
				_store.Reports.Add(new RmsIncidentReport
				{
					RmsIncidentReportId = reportId, DepartmentId = Dept, CallId = 77, ReportingEntityId = "FD24027000", DefinitionKey = RmsDefinitionKeys.NerisIncidentReport,
					DefinitionVersion = 1, LifecyclePreset = (int)RmsLifecyclePreset.QuickEntry, State = (int)reportState, RecordNumber = "INC-2026-0001", DraftReference = "I-ABCDE",
					IncidentNumber = "2026-000123", CurrentRevisionId = "rev-1", RevisionCount = 1, AuthorUserId = "author", OwnerUserId = "author", NerisIncidentId = externalId,
					CreatedOn = now, ModifiedOn = now, RowVersion = 3
				});
				_store.Projections.Add(new RmsRecordSearchProjection { RmsRecordSearchProjectionId = reportId, DepartmentId = Dept, RecordKind = (int)RmsRecordKind.IncidentReport, State = (int)reportState, SearchText = "INC-2026-0001", RowVersion = 1 });
			}

			var submission = new RmsSubmission
			{
				RmsSubmissionId = id, DepartmentId = Dept, RecordId = reportId, RecordKind = (int)RmsRecordKind.IncidentReport, RevisionId = "rev-1",
				Destination = RmsSubmissionDestinations.Neris, DestinationVersion = "1.4.78", IdempotencyKey = "key-" + id, State = (int)submissionState,
				MaxAttempts = maxAttempts, ExternalId = externalId, PayloadJson = "{}", PayloadChecksum = RecordSnapshotSerializer.Checksum("{}"),
				QueuedOn = now.AddMinutes(-5), CreatedByUserId = "author", CreatedOn = now, ModifiedOn = now, RowVersion = 1
			};
			_store.Submissions.Add(submission);
			return submission;
		}
	}
}
