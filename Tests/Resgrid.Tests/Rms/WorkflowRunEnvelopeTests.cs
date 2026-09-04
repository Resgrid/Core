using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Workflow run contract for Records events (RMS plan section 5.6): the run carries the immutable EventId and
	/// correlation data, a suppressed run is a durable Skipped row with its reason, and a duplicate-key race on
	/// (WorkflowId, EventId) is recognised as the dedup backstop rather than a failure.
	/// </summary>
	[TestFixture]
	public class WorkflowRunEnvelopeTests
	{
		private static DomainEventDispatchedEvent Envelope()
		{
			return new DomainEventDispatchedEvent
			{
				DepartmentId = 4, EventId = "evt-1", EventName = "RecordFinalized", SchemaVersion = 1, AggregateType = "RmsOperationalRecord",
				AggregateId = "rec-1", Sequence = 3, TriggerEventType = 104, CorrelationId = "rec-1", CausationId = "evt-0", OriginClient = 1
			};
		}

		[Test]
		public void The_envelope_lands_on_the_run()
		{
			var run = WorkflowRunEnvelope.Apply(new WorkflowRun { WorkflowRunId = "run-1", WorkflowId = "wf-1" }, Envelope());

			run.EventId.Should().Be("evt-1");
			run.EventSchemaVersion.Should().Be(1);
			run.CorrelationId.Should().Be("rec-1");
			run.CausationId.Should().Be("evt-0");
			run.RecordSequence.Should().Be(3);
			run.OriginClient.Should().Be(1);
			run.AggregateId.Should().Be("rec-1");
		}

		[Test]
		public void Legacy_events_leave_the_envelope_columns_empty()
		{
			var run = WorkflowRunEnvelope.Apply(new WorkflowRun { WorkflowRunId = "run-1" }, null);

			run.EventId.Should().BeNull();
			run.RecordSequence.Should().BeNull();
			run.OriginClient.Should().BeNull();
		}

		[Test]
		public void A_skipped_run_is_durable_and_carries_its_reason()
		{
			var now = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);
			var run = WorkflowRunEnvelope.MarkSkipped(WorkflowRunEnvelope.Apply(new WorkflowRun { WorkflowRunId = "run-1", Status = (int)WorkflowRunStatus.Pending, ErrorMessage = "x" }, Envelope()), WorkflowRunSkipReasons.RateLimit, now);

			run.Status.Should().Be((int)WorkflowRunStatus.Skipped);
			run.SkipReason.Should().Be("RateLimit");
			run.CompletedOn.Should().Be(now);
			run.ErrorMessage.Should().BeNull("a skip is an outcome, not an error");
			run.EventId.Should().Be("evt-1", "the skipped run still references the immutable event");
		}

		[TestCase("Cannot insert duplicate key row in object 'dbo.WorkflowRuns' with unique index 'UX_WorkflowRuns_Workflow_Event'.", true)]
		[TestCase("23505: duplicate key value violates unique constraint \"ux_workflowruns_workflow_event\"", true)]
		[TestCase("Timeout expired.", false)]
		[TestCase("", false)]
		public void Duplicate_key_violations_are_recognised_from_either_dialect(string message, bool expected)
		{
			WorkflowRunEnvelope.IsDuplicateKeyViolation(new InvalidOperationException("wrapper", new Exception(message))).Should().Be(expected);
		}
	}
}
