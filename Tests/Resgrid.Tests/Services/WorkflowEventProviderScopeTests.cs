using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using Moq;
using NUnit.Framework;
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
	/// WorkflowEventProvider is a singleton, so its scoped dependencies must come from a child scope per event.
	/// Captured at construction they lived in the root scope, where every concurrent event shared one unit of work:
	/// a Records run insert opened a transaction on it and the other handlers' queries collided on its connection
	/// ("The connection does not support MultipleActiveResultSets").
	/// </summary>
	[TestFixture, NonParallelizable]
	public sealed class WorkflowEventProviderScopeTests
	{
		/// <summary>A container carrying the provider's scoped dependencies, for tests that construct it directly.</summary>
		internal static IContainer Services(IWorkflowRepository workflows, IWorkflowRunRepository runs, IDepartmentsService departments,
			ISubscriptionsService subscriptions, IProtectedProjectionService projection, Lazy<IReadinessHistoryProtectionService> history = null)
		{
			var builder = new ContainerBuilder();
			builder.RegisterInstance(workflows).As<IWorkflowRepository>();
			builder.RegisterInstance(runs).As<IWorkflowRunRepository>();
			builder.RegisterInstance(departments).As<IDepartmentsService>();
			builder.RegisterInstance(subscriptions).As<ISubscriptionsService>();
			builder.RegisterInstance(projection).As<IProtectedProjectionService>();
			if (history != null)
				builder.Register(_ => history.Value).As<IReadinessHistoryProtectionService>();
			return builder.Build();
		}

		[Test]
		public async Task Concurrent_events_each_get_their_own_run_repository_scope()
		{
			// A department no other test touches, so the static per-minute rate limiter starts empty.
			var departmentId = 900000 + new Random().Next(99999);
			var trigger = WorkflowTriggerEventType.RecordCreated;

			var workflows = new Mock<IWorkflowRepository>();
			workflows.Setup(s => s.GetAllActiveByDepartmentAndEventTypeAsync(departmentId, (int)trigger))
				.ReturnsAsync(new[] { new Workflow { WorkflowId = Guid.NewGuid().ToString(), DepartmentId = departmentId, TriggerEventType = (int)trigger } });
			var subscriptions = new Mock<ISubscriptionsService>();
			subscriptions.Setup(s => s.GetCurrentPlanForDepartmentAsync(departmentId, It.IsAny<bool>())).ReturnsAsync(new Plan { PlanId = 999999 });
			var projection = new Mock<IProtectedProjectionService>();
			projection.Setup(s => s.BuildSafeWorkflowPayloadAsync(departmentId, It.IsAny<object>())).ReturnsAsync("{}");
			var queue = new Mock<IOutboundQueueProvider>();
			queue.Setup(s => s.EnqueueWorkflow(It.IsAny<WorkflowQueueItem>())).ReturnsAsync(true);

			var created = new List<Mock<IWorkflowRunRepository>>();
			var builder = new ContainerBuilder();
			builder.RegisterInstance(workflows.Object).As<IWorkflowRepository>();
			builder.RegisterInstance(Mock.Of<IDepartmentsService>()).As<IDepartmentsService>();
			builder.RegisterInstance(subscriptions.Object).As<ISubscriptionsService>();
			builder.RegisterInstance(projection.Object).As<IProtectedProjectionService>();
			builder.Register(_ =>
			{
				var runs = new Mock<IWorkflowRunRepository>();
				runs.Setup(s => s.InsertAsync(It.IsAny<WorkflowRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((WorkflowRun run, CancellationToken ct, bool first) => run);
				lock (created) created.Add(runs);
				return runs.Object;
			}).As<IWorkflowRunRepository>().InstancePerLifetimeScope();
			using var container = builder.Build();

			var bus = new EventAggregator();
			_ = new WorkflowEventProvider(bus, queue.Object, container);

			await Task.WhenAll(bus.SendMessageAsync(Dispatched(departmentId, trigger)), bus.SendMessageAsync(Dispatched(departmentId, trigger)));

			created.Should().HaveCount(2, "each event resolves its repositories (and unit of work) in its own child scope");
			created.Should().OnlyContain(runs => runs.Invocations.Count(i => i.Method.Name == nameof(IWorkflowRunRepository.InsertAsync)) == 1);
			queue.Verify(s => s.EnqueueWorkflow(It.IsAny<WorkflowQueueItem>()), Times.Exactly(2));
		}

		private static DomainEventDispatchedEvent Dispatched(int departmentId, WorkflowTriggerEventType trigger) => new DomainEventDispatchedEvent
		{
			DepartmentId = departmentId,
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
