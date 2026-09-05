using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Worker 42, the bounded RecordOverdue evaluation (RMS plan RMS-3). The behaviour the plan names explicitly is
	/// the one under test: the "at most once per record and due-state transition" guarantee is carried by the
	/// persisted RmsRecordDueState row, so a repeated run emits nothing new and a missed run still emits.
	/// </summary>
	[TestFixture]
	public class RecordsDueStateServiceTests
	{
		private const int Dept = 7;

		private FakeRmsStore _store;
		private FakeIncidentStore _incidents;
		private Mock<IOutboundQueueProvider> _queue;
		private List<NotificationItem> _notifications;
		private RecordsDueStateService _service;

		[SetUp]
		public void SetUp()
		{
			_store = new FakeRmsStore();
			_incidents = new FakeIncidentStore();
			_store.SeedActiveCutover(Dept);

			_notifications = new List<NotificationItem>();
			_queue = new Mock<IOutboundQueueProvider>();
			_queue.Setup(q => q.EnqueueNotification(It.IsAny<NotificationItem>()))
				.Returns((NotificationItem n) => { _notifications.Add(n); return Task.FromResult(true); });

			var outbox = new DomainEventOutboxService(_store.OutboxRepo.Object, Mock.Of<IEventAggregator>());

			_service = new RecordsDueStateService(_store.CutoversRepo.Object, _store.RecordsRepo.Object,
				_incidents.ReportsRepo.Object, _store.DueStatesRepo.Object, outbox, _queue.Object);
		}

		private RmsOperationalRecord SeedRecordAwaitingReview(DateTime? reviewDueOn, RmsRecordState state = RmsRecordState.ReadyForReview)
		{
			var record = new RmsOperationalRecord
			{
				RmsOperationalRecordId = Guid.NewGuid().ToString(),
				DepartmentId = Dept,
				ProtectionId = Guid.NewGuid().ToString(),
				DefinitionKey = RmsDefinitionKeys.Training,
				DefinitionVersion = 1,
				RecordType = (int)RmsOperationalRecordType.Training,
				State = (int)state,
				LifecyclePreset = (int)RmsLifecyclePreset.ReviewRequired,
				DraftReference = "D-1",
				AuthorUserId = "author",
				OwnerUserId = "author",
				ReviewerUserId = "reviewer",
				ReviewDueOn = reviewDueOn,
				CreatedOn = DateTime.UtcNow.AddDays(-5),
				ModifiedOn = DateTime.UtcNow.AddDays(-5),
				RowVersion = 1
			};
			_store.Records.Add(record);
			return record;
		}

		[Test]
		public async Task An_obligation_that_is_not_yet_due_emits_nothing()
		{
			SeedRecordAwaitingReview(DateTime.UtcNow.AddDays(5));

			var result = await _service.SweepAsync();

			result.BecameOverdue.Should().Be(0);
			_store.Outbox.Should().BeEmpty();
			_notifications.Should().BeEmpty();
			_store.DueStates.Should().ContainSingle().Which.LastEmittedState.Should().Be((int)RmsDueState.NotDue);
		}

		[Test]
		public async Task An_overdue_review_emits_trigger_112_and_notification_32_once()
		{
			var record = SeedRecordAwaitingReview(DateTime.UtcNow.AddHours(-30));

			var first = await _service.SweepAsync();

			first.BecameOverdue.Should().Be(1);
			_store.Outbox.Should().ContainSingle();
			_store.Outbox[0].TriggerEventType.Should().Be((int)WorkflowTriggerEventType.RecordOverdue);
			_notifications.Should().ContainSingle();
			_notifications[0].Type.Should().Be((int)EventTypes.RecordReviewOverdue);
			_notifications[0].Value.Should().Be(record.RmsOperationalRecordId + "|" + (int)RmsRecordObligation.Review);
			_notifications[0].UserId.Should().Be("reviewer", "a review obligation rests with the reviewer");

			var second = await _service.SweepAsync();

			second.BecameOverdue.Should().Be(0, "the persisted due state already says overdue");
			_store.Outbox.Should().ContainSingle("a repeated run must not double-emit");
			_notifications.Should().ContainSingle();
		}

		[Test]
		public async Task A_run_that_was_missed_still_emits_when_it_finally_happens()
		{
			// The record went overdue days ago and no sweep ran. Nothing here consults a last-run time, so the
			// emission is late rather than lost.
			SeedRecordAwaitingReview(DateTime.UtcNow.AddDays(-9));

			var result = await _service.SweepAsync();

			result.BecameOverdue.Should().Be(1);
			_store.Outbox.Should().ContainSingle();
		}

		[Test]
		public async Task A_moved_deadline_re_arms_the_emission()
		{
			var record = SeedRecordAwaitingReview(DateTime.UtcNow.AddHours(-2));
			await _service.SweepAsync();
			_store.Outbox.Should().ContainSingle();

			// The reviewer pushed the deadline out and it lapsed again: that is a different obligation, so it is
			// chased again rather than silently swallowed by the first emission.
			record.ReviewDueOn = DateTime.UtcNow.AddHours(-1);
			var second = await _service.SweepAsync();

			second.BecameOverdue.Should().Be(1);
			_store.Outbox.Should().HaveCount(2);
			_store.DueStates.Single().OverdueCount.Should().Be(2);
		}

		[Test]
		public async Task Meeting_the_obligation_clears_the_row_and_a_later_lapse_emits_again()
		{
			var record = SeedRecordAwaitingReview(DateTime.UtcNow.AddHours(-4));
			await _service.SweepAsync();
			_store.Outbox.Should().ContainSingle();

			// Reviewed: the record leaves ReadyForReview, so the obligation no longer applies.
			record.State = (int)RmsRecordState.Finalized;
			await _service.ClearForRecordAsync(Dept, record.RmsOperationalRecordId);
			_store.DueStates.Single().LastEmittedState.Should().Be((int)RmsDueState.Cleared);

			// Amended and sent back for review, and late again.
			record.State = (int)RmsRecordState.ReadyForReview;
			record.ReviewDueOn = DateTime.UtcNow.AddHours(-1);
			var third = await _service.SweepAsync();

			third.BecameOverdue.Should().Be(1);
			_store.Outbox.Should().HaveCount(2);
		}

		[Test]
		public async Task A_returned_record_becomes_overdue_only_after_the_correction_grace_period()
		{
			var record = SeedRecordAwaitingReview(null, RmsRecordState.Returned);
			record.ReturnedOn = DateTime.UtcNow.AddHours(-(RecordsDueStateService.CorrectionGraceHours - 1));

			(await _service.SweepAsync()).BecameOverdue.Should().Be(0);

			record.ReturnedOn = DateTime.UtcNow.AddHours(-(RecordsDueStateService.CorrectionGraceHours + 1));
			var result = await _service.SweepAsync();

			result.BecameOverdue.Should().Be(1);
			_notifications.Should().ContainSingle().Which.UserId.Should().Be("author", "a correction rests with the owner, not the reviewer");
			_store.DueStates.Single().Obligation.Should().Be((int)RmsRecordObligation.Correction);
		}

		[Test]
		public async Task An_obligation_with_no_deadline_is_never_overdue()
		{
			// A department that never set a review-due time has not made a promise the record can break.
			SeedRecordAwaitingReview(null);

			var result = await _service.SweepAsync();

			result.BecameOverdue.Should().Be(0);
			_store.Outbox.Should().BeEmpty();
		}

		[Test]
		public async Task The_event_payload_carries_the_obligation_and_no_record_content()
		{
			SeedRecordAwaitingReview(DateTime.UtcNow.AddHours(-25));

			await _service.SweepAsync();

			var payload = Newtonsoft.Json.Linq.JObject.Parse(_store.Outbox.Single().PayloadJson);
			payload["obligation"].Value<string>("type").Should().Be("Review");
			payload["obligation"].Value<int>("overdue_hours").Should().BeGreaterThanOrEqualTo(25);
			payload["obligation"].Value<string>("responsible_user_id").Should().Be("reviewer");
			payload["record"].Should().NotBeNull();
			payload.Should().NotContainKey("record_change", "an overdue event is not a lifecycle transition");
		}

		[Test]
		public async Task An_obligation_that_no_longer_applies_is_cleared_by_the_next_sweep_without_being_told()
		{
			var record = SeedRecordAwaitingReview(DateTime.UtcNow.AddHours(-4));
			await _service.SweepAsync();
			_store.DueStates.Single().LastEmittedState.Should().Be((int)RmsDueState.Overdue);

			// The reviewer finalized it and nothing called ClearForRecordAsync. The sweep must still reconcile,
			// otherwise a stale Overdue row would silence a later, genuine lapse.
			record.State = (int)RmsRecordState.Finalized;
			var second = await _service.SweepAsync();

			second.Cleared.Should().Be(1);
			_store.DueStates.Single().LastEmittedState.Should().Be((int)RmsDueState.Cleared);

			record.State = (int)RmsRecordState.ReadyForReview;
			record.ReviewDueOn = DateTime.UtcNow.AddHours(-1);
			(await _service.SweepAsync()).BecameOverdue.Should().Be(1);
			_store.Outbox.Should().HaveCount(2);
		}

		[Test]
		public async Task Emissions_per_department_are_bounded_and_the_remainder_is_chased_by_the_next_sweep()
		{
			for (var i = 0; i < RecordsDueStateService.MaxEmissionsPerDepartment + 10; i++)
				SeedRecordAwaitingReview(DateTime.UtcNow.AddHours(-5));

			var result = await _service.SweepAsync();

			result.BecameOverdue.Should().Be(RecordsDueStateService.MaxEmissionsPerDepartment, "only an emitted transition is recorded as one");
			_store.Outbox.Count.Should().Be(RecordsDueStateService.MaxEmissionsPerDepartment, "a department cannot flood the queue in one sweep");
			_store.DueStates.Count(d => d.LastEmittedState == (int)RmsDueState.Overdue)
				.Should().Be(RecordsDueStateService.MaxEmissionsPerDepartment, "a row the cap skipped must not read as already notified");

			// The cap is a trickle, not a silence: what the first sweep could not chase, the next one does.
			var second = await _service.SweepAsync();

			second.BecameOverdue.Should().Be(10);
			_store.Outbox.Count.Should().Be(RecordsDueStateService.MaxEmissionsPerDepartment + 10);
			(await _service.SweepAsync()).BecameOverdue.Should().Be(0, "every obligation has now been chased exactly once");
		}
	}
}
