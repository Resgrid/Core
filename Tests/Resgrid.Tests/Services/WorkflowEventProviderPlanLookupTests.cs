using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The plan read behind WorkflowEventProvider's rate limiting is a Billing API round trip, so it must only happen
	/// for an event that has an active workflow to run. Every in-process event (UserCreated, UnitAdded, Call*, ...)
	/// used to pay for it before the workflow lookup, even in departments with no workflows at all. The rate limits
	/// themselves, the paid-plan call-event exemption, and the Records skip rows are unchanged.
	/// </summary>
	[TestFixture, NonParallelizable]
	public sealed class WorkflowEventProviderPlanLookupTests
	{
		// The per-minute and daily trackers are static, keyed by department, so every test takes a department of its own.
		private static int _lastDepartmentId = 700000;

		private int _departmentId;
		private Mock<IWorkflowRepository> _workflows;
		private Mock<IWorkflowRunRepository> _runs;
		private Mock<ISubscriptionsService> _subscriptions;
		private Mock<IProtectedProjectionService> _projection;
		private Mock<IOutboundQueueProvider> _queue;
		private List<WorkflowRun> _inserted;
		private EventAggregator _bus;
		private Autofac.IContainer _container;

		[SetUp]
		public void SetUp()
		{
			_departmentId = Interlocked.Increment(ref _lastDepartmentId);
			_workflows = new Mock<IWorkflowRepository>();
			_runs = new Mock<IWorkflowRunRepository>();
			_subscriptions = new Mock<ISubscriptionsService>();
			_projection = new Mock<IProtectedProjectionService>();
			_queue = new Mock<IOutboundQueueProvider>();
			_inserted = new List<WorkflowRun>();

			_workflows.Setup(s => s.GetAllActiveByDepartmentAndEventTypeAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new Workflow[0]);
			_runs.Setup(s => s.InsertAsync(It.IsAny<WorkflowRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((WorkflowRun run, CancellationToken ct, bool first) => { _inserted.Add(run); return run; });
			_projection.Setup(s => s.BuildSafeWorkflowPayloadAsync(_departmentId, It.IsAny<object>())).ReturnsAsync("{}");
			_queue.Setup(s => s.EnqueueWorkflow(It.IsAny<WorkflowQueueItem>())).ReturnsAsync(true);

			_container = WorkflowEventProviderScopeTests.Services(_workflows.Object, _runs.Object, Mock.Of<IDepartmentsService>(), _subscriptions.Object, _projection.Object);
			_bus = new EventAggregator();
			_ = new WorkflowEventProvider(_bus, _queue.Object, _container);
		}

		[TearDown]
		public void TearDown() => _container?.Dispose();

		[TestCase(WorkflowTriggerEventType.UserCreated)]
		[TestCase(WorkflowTriggerEventType.UnitAdded)]
		[TestCase(WorkflowTriggerEventType.CallAdded)]
		[TestCase(WorkflowTriggerEventType.CertificationAdded)]
		public void Legacy_event_without_active_workflows_never_reads_the_plan(WorkflowTriggerEventType trigger)
		{
			Send(trigger);

			_workflows.Verify(s => s.GetAllActiveByDepartmentAndEventTypeAsync(_departmentId, (int)trigger), Times.Once);
			_subscriptions.Verify(s => s.GetCurrentPlanForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
			_projection.Verify(s => s.BuildSafeWorkflowPayloadAsync(It.IsAny<int>(), It.IsAny<object>()), Times.Never);
			_queue.Verify(s => s.EnqueueWorkflow(It.IsAny<WorkflowQueueItem>()), Times.Never);
			_inserted.Should().BeEmpty();
		}

		[Test]
		public void Legacy_event_with_an_active_workflow_reads_the_plan_once_and_enqueues_a_run()
		{
			var workflow = ActiveWorkflows(WorkflowTriggerEventType.UnitAdded).Single();
			GivenPlan(Plans.ProfessionalPlanId);

			Send(WorkflowTriggerEventType.UnitAdded);

			_subscriptions.Verify(s => s.GetCurrentPlanForDepartmentAsync(_departmentId, It.IsAny<bool>()), Times.Once);
			_queue.Verify(s => s.EnqueueWorkflow(It.Is<WorkflowQueueItem>(q => q.WorkflowId == workflow.WorkflowId && q.DepartmentId == _departmentId)), Times.Once);
			_inserted.Should().ContainSingle().Which.Status.Should().Be((int)WorkflowRunStatus.Pending);
		}

		[Test]
		public void Free_plan_limits_legacy_events_per_minute_without_exempting_call_events()
		{
			ActiveWorkflows(WorkflowTriggerEventType.CallAdded);
			GivenPlan(Plans.FreePlanId);
			var limit = WorkflowConfig.FreePlanRateLimitPerDepartmentPerMinute;

			for (var i = 0; i < limit + 2; i++)
				Send(WorkflowTriggerEventType.CallAdded);

			_queue.Verify(s => s.EnqueueWorkflow(It.IsAny<WorkflowQueueItem>()), Times.Exactly(limit));
			_inserted.Should().HaveCount(limit, "a rate-limited legacy event is dropped, never recorded as a skipped run");
			_inserted.Should().OnlyContain(r => r.Status == (int)WorkflowRunStatus.Pending);
		}

		[Test]
		public void Free_plan_stops_legacy_runs_at_the_daily_cap()
		{
			ActiveWorkflows(WorkflowTriggerEventType.UnitAdded);
			GivenPlan(Plans.FreePlanId);
			var previous = WorkflowConfig.FreePlanDailyRunLimit;
			WorkflowConfig.FreePlanDailyRunLimit = 2;
			try
			{
				for (var i = 0; i < 4; i++)
					Send(WorkflowTriggerEventType.UnitAdded);
			}
			finally
			{
				WorkflowConfig.FreePlanDailyRunLimit = previous;
			}

			_queue.Verify(s => s.EnqueueWorkflow(It.IsAny<WorkflowQueueItem>()), Times.Exactly(2));
		}

		[TestCase(WorkflowTriggerEventType.CallAdded)]
		[TestCase(WorkflowTriggerEventType.CallUpdated)]
		[TestCase(WorkflowTriggerEventType.CallClosed)]
		public void Paid_plan_call_events_are_exempt_from_the_per_minute_limit(WorkflowTriggerEventType trigger)
		{
			ActiveWorkflows(trigger);
			GivenPlan(Plans.ProfessionalPlanId);
			var events = WorkflowConfig.RateLimitPerDepartmentPerMinute + 5;

			for (var i = 0; i < events; i++)
				Send(trigger);

			_queue.Verify(s => s.EnqueueWorkflow(It.IsAny<WorkflowQueueItem>()), Times.Exactly(events));
		}

		[Test]
		public void Paid_plan_limits_other_legacy_events_per_minute()
		{
			ActiveWorkflows(WorkflowTriggerEventType.UnitAdded);
			GivenPlan(Plans.ProfessionalPlanId);
			var limit = WorkflowConfig.RateLimitPerDepartmentPerMinute;

			for (var i = 0; i < limit + 3; i++)
				Send(WorkflowTriggerEventType.UnitAdded);

			_queue.Verify(s => s.EnqueueWorkflow(It.IsAny<WorkflowQueueItem>()), Times.Exactly(limit));
		}

		[Test]
		public void Events_with_no_workflow_to_run_do_not_spend_the_departments_rate_limit()
		{
			// Only events that can start a run count against the limit now, as Records events already did.
			ActiveWorkflows(WorkflowTriggerEventType.CallAdded);
			GivenPlan(Plans.FreePlanId);
			var limit = WorkflowConfig.FreePlanRateLimitPerDepartmentPerMinute;

			for (var i = 0; i < limit * 3; i++)
				Send(WorkflowTriggerEventType.UserCreated);
			for (var i = 0; i < limit; i++)
				Send(WorkflowTriggerEventType.CallAdded);

			_subscriptions.Verify(s => s.GetCurrentPlanForDepartmentAsync(_departmentId, It.IsAny<bool>()), Times.Exactly(limit));
			_queue.Verify(s => s.EnqueueWorkflow(It.IsAny<WorkflowQueueItem>()), Times.Exactly(limit));
		}

		[Test]
		public async Task Records_event_without_active_workflows_never_reads_the_plan()
		{
			await _bus.SendMessageAsync(Dispatched(WorkflowTriggerEventType.RecordCreated));

			_workflows.Verify(s => s.GetAllActiveByDepartmentAndEventTypeAsync(_departmentId, (int)WorkflowTriggerEventType.RecordCreated), Times.Once);
			_subscriptions.Verify(s => s.GetCurrentPlanForDepartmentAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
			_projection.Verify(s => s.BuildSafeWorkflowPayloadAsync(It.IsAny<int>(), It.IsAny<object>()), Times.Never);
			_inserted.Should().BeEmpty();
		}

		[Test]
		public async Task Records_event_over_the_free_plan_limit_is_recorded_as_a_skipped_run_per_workflow()
		{
			var workflows = ActiveWorkflows(WorkflowTriggerEventType.RecordCreated, count: 2);
			GivenPlan(Plans.FreePlanId);
			var limit = WorkflowConfig.FreePlanRateLimitPerDepartmentPerMinute;

			for (var i = 0; i < limit; i++)
				await _bus.SendMessageAsync(Dispatched(WorkflowTriggerEventType.RecordCreated));
			var limited = Dispatched(WorkflowTriggerEventType.RecordCreated);
			await _bus.SendMessageAsync(limited);

			_subscriptions.Verify(s => s.GetCurrentPlanForDepartmentAsync(_departmentId, It.IsAny<bool>()), Times.Exactly(limit + 1));
			_queue.Verify(s => s.EnqueueWorkflow(It.IsAny<WorkflowQueueItem>()), Times.Exactly(limit * workflows.Length));
			_inserted.Where(r => r.Status == (int)WorkflowRunStatus.Pending).Should().HaveCount(limit * workflows.Length);

			var skipped = _inserted.Where(r => r.Status == (int)WorkflowRunStatus.Skipped).ToList();
			skipped.Select(r => r.WorkflowId).Should().BeEquivalentTo(workflows.Select(w => w.WorkflowId));
			skipped.Should().OnlyContain(r => r.SkipReason == WorkflowRunSkipReasons.RateLimit && r.EventId == limited.EventId && r.InputPayload == "{}");
		}

		private Workflow[] ActiveWorkflows(WorkflowTriggerEventType trigger, int count = 1)
		{
			var workflows = Enumerable.Range(0, count)
				.Select(_ => new Workflow { WorkflowId = Guid.NewGuid().ToString(), DepartmentId = _departmentId, TriggerEventType = (int)trigger, IsEnabled = true })
				.ToArray();
			_workflows.Setup(s => s.GetAllActiveByDepartmentAndEventTypeAsync(_departmentId, (int)trigger)).ReturnsAsync(workflows);
			return workflows;
		}

		private void GivenPlan(Plans plan) =>
			_subscriptions.Setup(s => s.GetCurrentPlanForDepartmentAsync(_departmentId, It.IsAny<bool>())).ReturnsAsync(new Plan { PlanId = (int)plan });

		/// <summary>
		/// Raises an in-process event. Its listener is async void, but every dependency here returns a completed task,
		/// so the handler has finished by the time SendMessage returns.
		/// </summary>
		private void Send(WorkflowTriggerEventType trigger)
		{
			switch (trigger)
			{
				case WorkflowTriggerEventType.UserCreated: _bus.SendMessage(new UserCreatedEvent { DepartmentId = _departmentId }); break;
				case WorkflowTriggerEventType.UnitAdded: _bus.SendMessage(new UnitAddedEvent { DepartmentId = _departmentId, Unit = new Unit() }); break;
				case WorkflowTriggerEventType.CallAdded: _bus.SendMessage(new CallAddedEvent { DepartmentId = _departmentId, Call = new Call() }); break;
				case WorkflowTriggerEventType.CallUpdated: _bus.SendMessage(new CallUpdatedEvent { DepartmentId = _departmentId, Call = new Call() }); break;
				case WorkflowTriggerEventType.CallClosed: _bus.SendMessage(new CallClosedEvent { DepartmentId = _departmentId, Call = new Call() }); break;
				case WorkflowTriggerEventType.CertificationAdded: _bus.SendMessage(new CertificationAddedEvent { DepartmentId = _departmentId }); break;
				default: throw new ArgumentOutOfRangeException(nameof(trigger), trigger, null);
			}
		}

		private DomainEventDispatchedEvent Dispatched(WorkflowTriggerEventType trigger) => new DomainEventDispatchedEvent
		{
			DepartmentId = _departmentId,
			EventId = Guid.NewGuid().ToString(),
			ProducerSubsystem = DomainEventProducers.Records,
			EventName = trigger.ToString(),
			SchemaVersion = 1,
			AggregateType = "OperationalRecord",
			AggregateId = Guid.NewGuid().ToString(),
			TriggerEventType = (int)trigger,
			PayloadJson = "{}",
			OccurredOn = DateTime.UtcNow
		};
	}
}
