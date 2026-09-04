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
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>Transactional event and delivery tests (RMS plan section 7): one row per committed transition, sequencing, retry with backoff, terminal parking.</summary>
	[TestFixture]
	public class DomainEventOutboxServiceTests
	{
		private FakeRmsStore _store;
		private Mock<IEventAggregator> _aggregator;
		private List<DomainEventDispatchedEvent> _published;
		private DomainEventOutboxService _service;

		[SetUp]
		public void SetUp()
		{
			_store = new FakeRmsStore();
			_published = new List<DomainEventDispatchedEvent>();
			_aggregator = new Mock<IEventAggregator>();
			_aggregator.Setup(a => a.SendMessage(It.IsAny<DomainEventDispatchedEvent>())).Callback<DomainEventDispatchedEvent>(e => _published.Add(e));
			_service = new DomainEventOutboxService(_store.OutboxRepo.Object, _aggregator.Object);
		}

		private static DomainEventEnvelope Envelope(string aggregateId = "rec-1", WorkflowTriggerEventType trigger = WorkflowTriggerEventType.RecordCreated)
		{
			return new DomainEventEnvelope { EventName = trigger.ToString(), AggregateType = "RmsOperationalRecord", AggregateId = aggregateId, Trigger = trigger, Payload = new { hello = "world" }, OriginClient = RmsOriginClient.Web };
		}

		[Test]
		public async Task Enqueue_assigns_identity_sequence_and_serialized_payload()
		{
			var first = await _service.EnqueueAsync(5, DomainEventProducers.Records, Envelope());
			var second = await _service.EnqueueAsync(5, DomainEventProducers.Records, Envelope(trigger: WorkflowTriggerEventType.RecordFinalized));
			var other = await _service.EnqueueAsync(5, DomainEventProducers.Records, Envelope(aggregateId: "rec-2"));

			Guid.TryParse(first.EventId, out _).Should().BeTrue();
			first.Sequence.Should().Be(1);
			second.Sequence.Should().Be(2, "sequence is per aggregate");
			other.Sequence.Should().Be(1);
			first.State.Should().Be((int)DomainEventOutboxState.Pending);
			first.TriggerEventType.Should().Be((int)WorkflowTriggerEventType.RecordCreated);
			first.PayloadJson.Should().Contain("\"hello\":\"world\"");
			first.Attempts.Should().Be(0);
		}

		[Test]
		public void Enqueue_rejects_an_envelope_without_event_name_or_aggregate()
		{
			Func<Task> noName = () => _service.EnqueueAsync(5, "Records", new DomainEventEnvelope { AggregateId = "x" });
			Func<Task> noAggregate = () => _service.EnqueueAsync(5, "Records", new DomainEventEnvelope { EventName = "RecordCreated" });

			noName.Should().ThrowAsync<ArgumentException>();
			noAggregate.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task Post_commit_dispatch_publishes_and_marks_the_row_once()
		{
			var entry = await _service.EnqueueAsync(5, DomainEventProducers.Records, Envelope());

			var dispatched = await _service.DispatchAfterCommitAsync(new[] { entry.DomainEventOutboxId, entry.DomainEventOutboxId });

			dispatched.Should().Be(1);
			_published.Should().HaveCount(1);
			_published[0].EventId.Should().Be(entry.EventId);
			_published[0].TriggerEventType.Should().Be((int)WorkflowTriggerEventType.RecordCreated);
			_published[0].IsReplay.Should().BeFalse();
			_store.Outbox.Single().State.Should().Be((int)DomainEventOutboxState.Dispatched);

			// A second sweep finds nothing pending.
			(await _service.DispatchPendingAsync("worker", 100)).Should().Be(0);
			_published.Should().HaveCount(1);
		}

		[Test]
		public async Task Failed_publish_is_retried_with_backoff_and_parked_after_the_cap()
		{
			_aggregator.Setup(a => a.SendMessage(It.IsAny<DomainEventDispatchedEvent>())).Throws(new InvalidOperationException("consumer down"));
			var entry = await _service.EnqueueAsync(5, DomainEventProducers.Records, Envelope());

			(await _service.DispatchPendingAsync("worker", 10)).Should().Be(0);
			var row = _store.Outbox.Single();
			row.State.Should().Be((int)DomainEventOutboxState.Pending, "the first failure schedules a retry");
			row.Attempts.Should().Be(1);
			row.NextAttemptOn.Should().NotBeNull();
			row.LastError.Should().Be("consumer down");

			// Exhaust the attempts: each sweep claims (attempts++) then fails.
			for (var i = 1; i < DomainEventOutboxService.MaximumAttempts; i++)
			{
				row.NextAttemptOn = null;
				await _service.DispatchPendingAsync("worker", 10);
			}

			row.Attempts.Should().Be(DomainEventOutboxService.MaximumAttempts);
			row.State.Should().Be((int)DomainEventOutboxState.Failed, "delivery exhausted its retries and requires operator attention");
			row.NextAttemptOn.Should().BeNull();

			var health = await _service.GetHealthAsync();
			health.Failed.Should().Be(1);
			health.Pending.Should().Be(0);
		}

		[Test]
		public void Backoff_grows_geometrically_and_caps_at_six_hours()
		{
			DomainEventOutboxService.BackoffFor(1).Should().Be(TimeSpan.FromMinutes(1));
			DomainEventOutboxService.BackoffFor(2).Should().Be(TimeSpan.FromMinutes(4));
			DomainEventOutboxService.BackoffFor(3).Should().Be(TimeSpan.FromMinutes(16));
			DomainEventOutboxService.BackoffFor(8).Should().Be(TimeSpan.FromHours(6));
			DomainEventOutboxService.BackoffFor(20).Should().Be(TimeSpan.FromHours(6));
		}

		[Test]
		public async Task Sweep_dispatches_in_outbox_order_and_marks_replays()
		{
			var a = await _service.EnqueueAsync(5, DomainEventProducers.Records, Envelope());
			var b = await _service.EnqueueAsync(5, DomainEventProducers.Records, Envelope(trigger: WorkflowTriggerEventType.RecordFinalized));
			a.Attempts = 1; // simulate a prior crashed attempt

			(await _service.DispatchPendingAsync("worker", 100)).Should().Be(2);

			_published.Select(p => p.EventId).Should().Equal(a.EventId, b.EventId);
			_published[0].IsReplay.Should().BeTrue();
			_published[1].IsReplay.Should().BeFalse();
			_published.Select(p => p.Sequence).Should().Equal(1, 2);
		}
	}
}
