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
		private Mock<IRecordsAuthorizationService> _authorization;
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
			// Database reads/writes produce detached objects; sharing them would hide lease-version races.
			_store.SubmissionsRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsSubmission>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsSubmission s, CancellationToken c, bool force) =>
				{
					_store.Submissions.RemoveAll(x => x.RmsSubmissionId == s.RmsSubmissionId);
					_store.Submissions.Add(Newtonsoft.Json.JsonConvert.DeserializeObject<RmsSubmission>(Newtonsoft.Json.JsonConvert.SerializeObject(s)));
					return s;
				});

			_profile = new RmsNerisProfile { DepartmentId = Dept, NerisEntityId = "FD24027000", ContractVersion = "1.4.78", IsEnabled = true };
			_enabled = true;
			_profiles = new Mock<INerisProfileService>();
			_profiles.Setup(p => p.GetProfileAsync(Dept)).ReturnsAsync(() => _profile);
			_profiles.Setup(p => p.IsSubmissionEnabledAsync(Dept)).ReturnsAsync(() => _enabled);
			_profiles.Setup(p => p.GetDestinationIdentity(It.IsAny<RmsNerisProfile>())).Returns("test-destination");

			_delivery = new Mock<INerisSubmissionService>();
			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.HasPermissionAsync(It.IsAny<string>(), Dept, PermissionTypes.SubmitRecords)).ReturnsAsync(true);
			_authorization.Setup(a => a.CanUserViewRecordAsync(It.IsAny<string>(), It.IsAny<string>(), Dept)).ReturnsAsync(true);

			_notifications = new List<NotificationItem>();
			_outboundQueue = new Mock<IOutboundQueueProvider>();
			_outboundQueue.Setup(q => q.EnqueueNotification(It.IsAny<NotificationItem>())).Callback<NotificationItem>(n => _notifications.Add(n)).ReturnsAsync(true);

			var outbox = new DomainEventOutboxService(_store.Shared.OutboxRepo.Object, new Mock<IEventAggregator>().Object);
			_service = new RecordsSubmissionService(_store.SubmissionsRepo.Object, _store.ReportsRepo.Object, _store.AnalysesRepo.Object, _store.Shared.ProjectionsRepo.Object,
				_store.Shared.AuditsRepo.Object, _profiles.Object, _delivery.Object, outbox, _outboundQueue.Object, _store.UnitOfWork.Object, _store.Shared.CutoversRepo.Object, Mock.Of<IIncidentAnalysisService>(), _store.ExchangesRepo.Object, _authorization.Object);
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
			_delivery.Setup(d => d.DeliverAsync(_profile, It.Is<RmsSubmission>(s => s.RmsSubmissionId == submission.RmsSubmissionId), null, It.IsAny<CancellationToken>()))
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
			_delivery.Setup(d => d.DeliverAsync(_profile, It.Is<RmsSubmission>(s => s.RmsSubmissionId == submission.RmsSubmissionId), null, It.IsAny<CancellationToken>()))
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
			_delivery.Setup(d => d.DeliverAsync(_profile, It.Is<RmsSubmission>(s => s.RmsSubmissionId == submission.RmsSubmissionId), null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Transient, StatusCode = 503, Message = "NERIS returned 503." });

			var first = await _service.ProcessAsync(submission);
			first.State.Should().Be((int)RmsSubmissionState.Queued);
			first.Attempts.Should().Be(1);
			first.NextAttemptOn.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(RecordsSubmissionService.Backoff(1)), TimeSpan.FromSeconds(10));
			first.ErrorSummary.Should().Be("NERIS returned 503.");
			first.LeaseOwner.Should().BeNull();
			_store.Reports.Single().State.Should().Be((int)RmsRecordState.Finalized, "a deferred delivery leaves the report where it was");
			_store.Outbox.Should().BeEmpty();

			var second = await _service.ProcessAsync(Lease(_store.Submissions.Single()));
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
			_delivery.Setup(d => d.DeliverAsync(_profile, It.Is<RmsSubmission>(s => s.RmsSubmissionId == submission.RmsSubmissionId), null, It.IsAny<CancellationToken>()))
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
			Lease(submission);

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
			foreach (var row in _store.Submissions) { row.LeaseOwner = null; row.LeaseExpiresOn = null; }
			_delivery.Setup(d => d.DeliverAsync(It.IsAny<RmsNerisProfile>(), It.IsAny<RmsSubmission>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((RmsNerisProfile p, RmsSubmission s, string existing, CancellationToken c) => new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Created, StatusCode = 201, ExternalId = "FD24027000I" + s.RmsSubmissionId, ExternalStatus = "SUBMITTED" });

			var result = await _service.SweepAsync();

			result.Claimed.Should().Be(2);
			result.Delivered.Should().Be(2);
			result.Errors.Should().Be(0);
			_store.Submissions.Single(s => s.RmsSubmissionId == "a").State.Should().Be((int)RmsSubmissionState.AwaitingDestination);
			_store.Submissions.Single(s => s.RmsSubmissionId == "b").State.Should().Be((int)RmsSubmissionState.AwaitingDestination);
			notDue.State.Should().Be((int)RmsSubmissionState.Queued);
			result.Message.Should().Contain("claimed 2");
		}

		[Test]
		public async Task Sweep_is_a_no_op_while_NERIS_is_disabled()
		{
			NerisConfig.Enabled = false;
			Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued);
			_store.Submissions.Single().LeaseOwner = null;

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
					LastSubmissionId = id,
					CreatedOn = now, ModifiedOn = now, RowVersion = 3
				});
				_store.Projections.Add(new RmsRecordSearchProjection { RmsRecordSearchProjectionId = reportId, DepartmentId = Dept, RecordKind = (int)RmsRecordKind.IncidentReport, State = (int)reportState, SearchText = "INC-2026-0001", RowVersion = 1 });
			}

			var submission = new RmsSubmission
			{
				RmsSubmissionId = id, DepartmentId = Dept, RecordId = reportId, RecordKind = (int)RmsRecordKind.IncidentReport, RevisionId = "rev-1",
				Destination = RmsSubmissionDestinations.Neris, DestinationVersion = "1.4.78", IdempotencyKey = "key-" + id, State = (int)submissionState,
				DestinationIdentity = "test-destination",
				MaxAttempts = maxAttempts, ExternalId = externalId, PayloadJson = "{}", PayloadChecksum = RecordSnapshotSerializer.Checksum("{}"),
				QueuedOn = now.AddMinutes(-5), CreatedByUserId = "author", CreatedOn = now, ModifiedOn = now, RowVersion = 1
			};
			_store.Submissions.Add(submission);
			return Lease(submission);
		}

		private static RmsSubmission Lease(RmsSubmission submission)
		{
			submission.LeaseOwner = "test-worker";
			submission.LeaseExpiresOn = DateTime.UtcNow.AddMinutes(5);
			submission.RowVersion++;
			return submission;
		}

		[Test]
		public async Task Expired_or_replaced_lease_performs_no_destination_call()
		{
			var row = Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued);
			row.LeaseExpiresOn = DateTime.UtcNow.AddMinutes(-1);
			await _service.ProcessAsync(row);
			_store.Exchanges.Should().BeEmpty();
			_delivery.Invocations.Should().BeEmpty();
		}

		[TestCase(true)]
		[TestCase(false)]
		public async Task Altered_payload_or_destination_fails_before_network(bool payload)
		{
			var row = Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued);
			if (payload) row.PayloadJson = "{\"changed\":true}";
			else row.DestinationIdentity = "another-environment";
			var result = await _service.ProcessAsync(row);
			result.State.Should().Be((int)RmsSubmissionState.Failed);
			_delivery.Invocations.Should().BeEmpty();
		}

		[Test]
		public async Task A_saved_receipt_is_replayed_after_losing_the_lease_without_repeating_create()
		{
			var row = Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued);
			_delivery.Setup(d => d.DeliverAsync(_profile, It.IsAny<RmsSubmission>(), null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() =>
				{
					_store.Submissions.Single().LeaseExpiresOn = DateTime.UtcNow.AddMinutes(-1);
					return new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Created, ExternalId = "remote-1", StatusCode = 201, ResponseJson = "{\"neris_id\":\"remote-1\"}" };
				});
			await _service.ProcessAsync(row);
			_store.Reports.Single().NerisIncidentId.Should().BeNull();
			_store.Exchanges.Select(e => e.Stage).Should().Equal("Started", "Response");

			var recovered = await _service.ProcessAsync(Lease(_store.Submissions.Single()));
			recovered.ExternalId.Should().Be("remote-1");
			recovered.State.Should().Be((int)RmsSubmissionState.AwaitingDestination);
			recovered.CreatePendingReceipt.Should().BeFalse();
			_store.Exchanges.Select(e => e.Stage).Should().Equal("Started", "Response", "Applied");
			_delivery.Verify(d => d.DeliverAsync(_profile, It.IsAny<RmsSubmission>(), null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Late_success_recovers_after_another_worker_marks_the_create_uncertain()
		{
			var row = Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued);
			var response = new TaskCompletionSource<NerisSubmissionOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
			var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			_delivery.Setup(d => d.DeliverAsync(_profile, It.IsAny<RmsSubmission>(), null, It.IsAny<CancellationToken>()))
				.Returns(() => { started.SetResult(true); return response.Task; });
			var first = _service.ProcessAsync(row);
			await started.Task;
			var leased = _store.Submissions.Single();
			leased.LeaseExpiresOn = DateTime.UtcNow.AddMinutes(-1);
			var uncertain = await _service.ProcessAsync(Lease(leased));
			uncertain.RequiresReconciliation.Should().BeTrue();
			_store.Exchanges.Should().NotContain(e => e.Stage == "Applied");
			response.SetResult(new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Created, StatusCode = 201, ExternalId = "late-remote", ResponseJson = "{\"neris_id\":\"late-remote\"}" });
			await first;

			var sweep = await _service.SweepAsync();
			sweep.Delivered.Should().Be(1);
			_store.Reports.Single().NerisIncidentId.Should().Be("late-remote");
			_store.Submissions.Single().RequiresReconciliation.Should().BeFalse();
			_delivery.Verify(d => d.DeliverAsync(_profile, It.IsAny<RmsSubmission>(), null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_late_response_preserves_a_concurrent_amendment_draft()
		{
			var row = Seed(RmsRecordState.Submitted, RmsSubmissionState.AwaitingDestination, externalId: "remote-1");
			_delivery.Setup(d => d.CheckStatusAsync(_profile, "remote-1", It.IsAny<CancellationToken>())).ReturnsAsync(() =>
			{
				var report = _store.Reports.Single();
				report.AmendsRevisionId = report.CurrentRevisionId;
				report.DisplaySummary = "new draft content";
				report.RowVersion++;
				return new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Accepted, ExternalId = "remote-1", StatusCode = 200 };
			});
			await _service.ProcessAsync(row);
			var edited = _store.Reports.Single();
			edited.DisplaySummary.Should().Be("new draft content");
			edited.AmendsRevisionId.Should().Be("rev-1");
			edited.State.Should().Be((int)RmsRecordState.Submitted);
		}

		[Test]
		public async Task Superseding_the_submission_during_HTTP_prevents_all_stale_aggregate_writes()
		{
			var row = Seed(RmsRecordState.Submitted, RmsSubmissionState.AwaitingDestination, externalId: "remote-1");
			_delivery.Setup(d => d.CheckStatusAsync(_profile, "remote-1", It.IsAny<CancellationToken>())).ReturnsAsync(() =>
			{
				_store.Reports.Single().CurrentRevisionId = "rev-2";
				_store.Reports.Single().State = (int)RmsRecordState.Voided;
				_store.Submissions.Single().State = (int)RmsSubmissionState.Superseded;
				_store.Submissions.Single().RowVersion++;
				return new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Accepted, ExternalId = "remote-1", StatusCode = 200 };
			});
			await _service.ProcessAsync(row);
			_store.Reports.Single().State.Should().Be((int)RmsRecordState.Voided);
			_store.Reports.Single().CurrentRevisionId.Should().Be("rev-2");
			_store.Outbox.Should().BeEmpty();
			_store.Exchanges.Should().ContainSingle(e => e.Stage == "Response");
		}

		[TestCase(202, NerisOutcomeKind.Transient)]
		[TestCase(201, NerisOutcomeKind.Rejected)]
		[TestCase(201, NerisOutcomeKind.Fatal)]
		public async Task Successful_create_without_ID_never_automatically_repeats(int status, NerisOutcomeKind kind)
		{
			var row = Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued);
			_delivery.Setup(d => d.DeliverAsync(_profile, It.IsAny<RmsSubmission>(), null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new NerisSubmissionOutcome { Kind = kind, StatusCode = status, ResponseJson = "{}" });
			var result = await _service.ProcessAsync(row);
			result.RequiresReconciliation.Should().BeTrue();
			result.CreatePendingReceipt.Should().BeTrue();
			(await _service.SweepAsync()).Claimed.Should().Be(0);
			_delivery.Verify(d => d.DeliverAsync(_profile, It.IsAny<RmsSubmission>(), null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[TestCase(true)]
		[TestCase(false)]
		public async Task Reconciliation_rejects_wrong_incident_or_revoked_submit_permission(bool wrongIncident)
		{
			var row = SeedUncertain();
			if (wrongIncident)
				_delivery.Setup(d => d.CheckStatusAsync(_profile, "known-id", It.IsAny<CancellationToken>())).ReturnsAsync(new NerisSubmissionOutcome
				{ StatusCode = 200, ExternalId = "known-id", ResponseJson = "{\"neris_id\":\"known-id\",\"base\":{\"incident_number\":\"OTHER-INCIDENT\"}}" });
			else _authorization.Setup(a => a.HasPermissionAsync("officer", Dept, PermissionTypes.SubmitRecords)).ReturnsAsync(false);
			Func<Task> act = () => _service.ReconcileAsync(Dept, "officer", row.RmsSubmissionId, row.RowVersion, "known-id", "Checked the destination filing.");
			if (wrongIncident) await act.Should().ThrowAsync<InvalidOperationException>();
			else await act.Should().ThrowAsync<UnauthorizedAccessException>();
			_store.Submissions.Single().RequiresReconciliation.Should().BeTrue();
			_store.Exchanges.Should().BeEmpty();
		}

		[Test]
		public async Task Reconciliation_verifies_the_receipt_and_resumes_status_polling_without_creating()
		{
			var row = SeedUncertain();
			_delivery.Setup(d => d.CheckStatusAsync(_profile, "known-id", It.IsAny<CancellationToken>())).ReturnsAsync(new NerisSubmissionOutcome
			{ Kind = NerisOutcomeKind.Accepted, StatusCode = 200, ExternalId = "known-id", ResponseJson = MatchingReceipt() });
			await _service.ReconcileAsync(Dept, "officer", row.RmsSubmissionId, row.RowVersion, "known-id", "Verified matching number in NERIS.");
			_store.Submissions.Single().RequiresReconciliation.Should().BeFalse();
			_store.Submissions.Single().ExternalId.Should().Be("known-id");
			_store.Exchanges.Should().ContainSingle(e => e.Stage == "Reconciled" && e.OutcomeChecksum == RecordSnapshotSerializer.Checksum(e.OutcomeJson));
			_store.Audits.Should().ContainSingle(a => a.ActorUserId == "officer" && a.DetailJson.Contains("Verified matching number"));
			var sweep = await _service.SweepAsync();
			sweep.Accepted.Should().Be(1);
			_store.Reports.Single().NerisIncidentId.Should().Be("known-id");
			_delivery.Verify(d => d.DeliverAsync(It.IsAny<RmsNerisProfile>(), It.IsAny<RmsSubmission>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task An_external_ID_from_another_destination_cannot_be_reused_for_an_amendment()
		{
			var row = Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued);
			_store.Reports.Single().NerisIncidentId = "sandbox-id";
			_store.Submissions.Add(new RmsSubmission { RmsSubmissionId = "old-sandbox", DepartmentId = Dept, RecordId = ReportId,
				ExternalId = "sandbox-id", DestinationIdentity = "sandbox", State = (int)RmsSubmissionState.Accepted });
			var result = await _service.ProcessAsync(row);
			result.State.Should().Be((int)RmsSubmissionState.Failed);
			result.ErrorSummary.Should().Contain("another destination");
			_delivery.Invocations.Should().BeEmpty();
		}

		private RmsSubmission SeedUncertain()
		{
			var row = Seed(RmsRecordState.Finalized, RmsSubmissionState.Failed);
			row.RequiresReconciliation = true; row.CreatePendingReceipt = true; row.LeaseOwner = null; row.LeaseExpiresOn = null;
			row.PayloadJson = "{\"base\":{\"department_neris_id\":\"FD24027000\",\"incident_number\":\"2026-000123\"},\"dispatch\":{\"call_create\":\"2026-09-01T12:00:00Z\"}}";
			row.PayloadChecksum = RecordSnapshotSerializer.Checksum(row.PayloadJson);
			return row;
		}

		private static string MatchingReceipt() => "{\"neris_id\":\"known-id\",\"base\":{\"department_neris_id\":\"FD24027000\",\"incident_number\":\"2026-000123\"},\"dispatch\":{\"call_create\":1788264000}}";

		[Test]
		public async Task Unsent_legacy_submission_requires_explicit_binding_and_does_not_send_during_binding()
		{
			var row = Seed(RmsRecordState.Finalized, RmsSubmissionState.Queued);
			row.DestinationIdentity = null; row.LeaseOwner = null; row.LeaseExpiresOn = null;
			await _service.ReconcileAsync(Dept, "officer", row.RmsSubmissionId, row.RowVersion, null, "Reviewed department entity and destination.");
			_store.Submissions.Single().DestinationIdentity.Should().Be("test-destination");
			_store.Audits.Should().ContainSingle(a => a.Purpose == "Bind unsent legacy submission");
			_delivery.Invocations.Should().BeEmpty();
		}

		[Test]
		public async Task Sent_legacy_submission_cannot_use_the_unsent_binding_path()
		{
			var row = SeedUncertain();
			row.DestinationIdentity = null; row.SentOn = DateTime.UtcNow.AddDays(-1);
			Func<Task> act = () => _service.ReconcileAsync(Dept, "officer", row.RmsSubmissionId, row.RowVersion, null, "Attempted to bind.");
			await act.Should().ThrowAsync<ArgumentException>();
			_store.Submissions.Single().RequiresReconciliation.Should().BeTrue();
			_delivery.Invocations.Should().BeEmpty();
		}

		[Test]
		public async Task Legacy_known_receipt_is_verified_before_binding_to_current_destination()
		{
			var row = SeedUncertain(); row.DestinationIdentity = null; row.ExternalId = "known-id";
			_delivery.Setup(d => d.CheckStatusAsync(_profile, "known-id", It.IsAny<CancellationToken>())).ReturnsAsync(new NerisSubmissionOutcome
			{ Kind = NerisOutcomeKind.Accepted, StatusCode = 200, ExternalId = "known-id", ResponseJson = MatchingReceipt() });
			await _service.ReconcileAsync(Dept, "officer", row.RmsSubmissionId, row.RowVersion, "known-id", "Verified legacy receipt.");
			_store.Submissions.Single().DestinationIdentity.Should().Be("test-destination");
			_store.Submissions.Single().RequiresReconciliation.Should().BeFalse();
		}

		[Test]
		public async Task Externally_verified_absence_preserves_exchange_history_and_never_automatically_retries()
		{
			var row = SeedUncertain(); var payload = row.PayloadJson; var version = row.RowVersion;
			_authorization.Setup(a => a.IsDepartmentAdminAsync("administrator", Dept)).ReturnsAsync(true);
			_store.Exchanges.Add(new RmsSubmissionExchange { DepartmentId = Dept, SubmissionId = row.RmsSubmissionId, RecordId = ReportId, ExchangeId = "ambiguous-create", Operation = "Create", Stage = "Started", DestinationIdentity = row.DestinationIdentity, PayloadChecksum = row.PayloadChecksum });
			await _service.ConfirmNotCreatedAsync(Dept, "administrator", row.RmsSubmissionId, version, "Destination support case 123", "Support confirmed the attempted filing does not exist.");
			var saved = _store.Submissions.Single(); saved.RequiresReconciliation.Should().BeFalse(); saved.CreatePendingReceipt.Should().BeFalse();
			saved.State.Should().Be((int)RmsSubmissionState.Rejected); saved.NextAttemptOn.Should().BeNull(); saved.PayloadJson.Should().Be(payload);
			_store.Exchanges.Should().ContainSingle(e => e.Stage == "Started");
			_store.Exchanges.Should().ContainSingle(e => e.Stage == "Reconciled" && e.ExchangeId == "ambiguous-create");
			_store.Exchanges.Should().ContainSingle(e => e.Operation == "ConfirmNotCreated" && e.OutcomeJson.Contains("Destination support case 123"));
			_store.Audits.Should().ContainSingle(a => a.ActorUserId == "administrator" && a.Purpose == "Destination absence externally verified");
			(await _service.SweepAsync()).Claimed.Should().Be(0); _delivery.Invocations.Should().BeEmpty();
			Func<Task> replay = () => _service.ConfirmNotCreatedAsync(Dept, "administrator", row.RmsSubmissionId, version, "Case 123", "Repeat");
			await replay.Should().ThrowAsync<RecordConcurrencyException>();
		}

		[TestCase("permission")]
		[TestCase("reference")]
		[TestCase("scope")]
		[TestCase("destination")]
		[TestCase("receipt")]
		[TestCase("lease")]
		[TestCase("checksum")]
		public async Task Absence_confirmation_cannot_bypass_administrator_source_scope_receipt_or_worker_fences(string boundary)
		{
			var row = SeedUncertain(); var reference = "Destination support case 123";
			_authorization.Setup(a => a.IsDepartmentAdminAsync("administrator", Dept)).ReturnsAsync(boundary != "permission");
			if (boundary == "reference") reference = " ";
			if (boundary == "scope") _authorization.Setup(a => a.CanUserViewRecordAsync("administrator", ReportId, Dept)).ReturnsAsync(false);
			if (boundary == "destination") row.DestinationIdentity = "other-destination";
			if (boundary == "receipt") row.ExternalId = "known-filing";
			if (boundary == "lease") row.LeaseExpiresOn = DateTime.UtcNow.AddMinutes(3);
			if (boundary == "checksum") row.PayloadJson += " ";
			Func<Task> confirm = () => _service.ConfirmNotCreatedAsync(Dept, "administrator", row.RmsSubmissionId, row.RowVersion, reference, "Verified externally");
			if (boundary == "permission" || boundary == "scope") await confirm.Should().ThrowAsync<UnauthorizedAccessException>();
			else if (boundary == "reference") await confirm.Should().ThrowAsync<ArgumentException>();
			else if (boundary == "lease") await confirm.Should().ThrowAsync<RecordConcurrencyException>();
			else await confirm.Should().ThrowAsync<InvalidOperationException>();
			_store.Submissions.Single().RequiresReconciliation.Should().BeTrue(); _store.Exchanges.Should().BeEmpty(); _delivery.Invocations.Should().BeEmpty();
		}

		[Test]
		public async Task Legacy_rejected_without_a_filing_ID_can_be_explicitly_bound_after_external_verification()
		{
			var row = SeedUncertain(); row.DestinationIdentity = null; row.RequiresReconciliation = false; row.CreatePendingReceipt = false;
			row.State = (int)RmsSubmissionState.Rejected; row.ResponseStatusCode = 422; row.ResponseJson = "{\"detail\":\"Invalid incident\"}"; row.ResponseChecksum = RecordSnapshotSerializer.Checksum(row.ResponseJson);
			var originalResponse = row.ResponseJson;
			_authorization.Setup(a => a.IsDepartmentAdminAsync("administrator", Dept)).ReturnsAsync(true);
			await _service.ConfirmNotCreatedAsync(Dept, "administrator", row.RmsSubmissionId, row.RowVersion, "NERIS verification case 456", "Verified legacy rejection with no filing.");
			_store.Submissions.Single().DestinationIdentity.Should().Be("test-destination"); _store.Submissions.Single().ResponseJson.Should().Be(originalResponse);
			_store.Submissions.Single().NextAttemptOn.Should().BeNull(); _delivery.Invocations.Should().BeEmpty();
		}

		[Test]
		public async Task Exchange_history_rechecks_restricted_access_after_loading_responses()
		{
			var row = SeedUncertain(); var allowed = true;
			_authorization.Setup(a => a.HasPermissionAsync("officer", Dept, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(() => allowed);
			_store.ExchangesRepo.Setup(e => e.GetForSubmissionAsync(Dept, row.RmsSubmissionId)).Callback(() => allowed = false).ReturnsAsync(new List<RmsSubmissionExchange>());
			Func<Task> read = () => _service.GetHistoryAsync(Dept, "officer", row.RmsSubmissionId);
			await read.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[Test]
		public async Task Exchange_history_rejects_a_changed_response_instead_of_presenting_it_as_destination_evidence()
		{
			var row = SeedUncertain();
			_authorization.Setup(a => a.HasPermissionAsync("officer", Dept, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(true);
			_store.Exchanges.Add(new RmsSubmissionExchange { DepartmentId = Dept, SubmissionId = row.RmsSubmissionId, OutcomeJson = "{\"forged\":true}", OutcomeChecksum = RecordSnapshotSerializer.Checksum("{}") });
			Func<Task> read = () => _service.GetHistoryAsync(Dept, "officer", row.RmsSubmissionId);
			await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*integrity*");
		}

		[Test]
		public async Task Reconciliation_rejects_reused_incident_number_from_a_different_date()
		{
			var row = SeedUncertain();
			var receipt = JObject.Parse(MatchingReceipt());
			receipt["dispatch"]["call_create"] = "2025-09-01T12:00:00Z";
			_delivery.Setup(d => d.CheckStatusAsync(_profile, "known-id", It.IsAny<CancellationToken>())).ReturnsAsync(new NerisSubmissionOutcome
			{ StatusCode = 200, ResponseJson = receipt.ToString() });
			Func<Task> act = () => _service.ReconcileAsync(Dept, "officer", row.RmsSubmissionId, row.RowVersion, "known-id", "Looked up incident number.");
			await act.Should().ThrowAsync<InvalidOperationException>();
			_store.Submissions.Single().RequiresReconciliation.Should().BeTrue();
			_store.Exchanges.Should().BeEmpty();
		}
	}
}
