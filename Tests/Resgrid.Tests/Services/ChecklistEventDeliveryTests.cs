using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Services;
using Resgrid.Services.Records;
using Resgrid.Tests.Rms;
using Scriban;
using Scriban.Runtime;

namespace Resgrid.Tests.Services
{
	[TestFixture, NonParallelizable]
	public partial class ChecklistEventDeliveryTests
	{
		private FakeRmsStore _store;
		private EventAggregator _bus;
		private Mock<IDepartmentDataProtectionService> _policy;
		private ProtectedProjectionService _projection;
		private DomainEventOutboxService _outbox;
		private ReadinessHistoryTestProtection _history;
		[TearDown] public void TearDown() => _history?.Dispose();
		[SetUp]
		public void SetUp()
		{
			_store = new FakeRmsStore(); _bus = new EventAggregator(); _policy = new Mock<IDepartmentDataProtectionService>();
			_projection = new ProtectedProjectionService(_policy.Object, new ProtectedFieldCatalog());
			_history = new ReadinessHistoryTestProtection(_policy);
			_outbox = new DomainEventOutboxService(_store.OutboxRepo.Object, _bus, new Lazy<IProtectedProjectionService>(() => _projection), _history.Lazy);
			_store.OutboxRepo.Setup(s => s.InitializeChecklistPayloadAsync(It.IsAny<DomainEventOutboxEntry>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_store.OutboxRepo.Setup(s => s.ReplaceChecklistPayloadAsync(It.IsAny<DomainEventOutboxEntry>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DomainEventOutboxEntry e, string payload, CancellationToken ct) => { e.PayloadJson = payload; return true; });
		}
		private static DomainEventEnvelope Event() => new DomainEventEnvelope
		{
			EventName = "ChecklistCompleted", AggregateType = "ChecklistCompletion", AggregateId = Guid.NewGuid().ToString(), Trigger = WorkflowTriggerEventType.ChecklistCompleted,
			Payload = new { CompletionId = Guid.NewGuid().ToString(), DefinitionId = Guid.NewGuid().ToString(), VersionId = Guid.NewGuid().ToString(), TargetType = 3, TargetId = "person-42", Score = 87.25m, Passed = false, Note = "SYNTHETIC-PHI-CANARY", Data = new byte[] { 9, 8, 7 } }
		};
		[Test]
		public async Task Projection_precedes_persistence_and_is_rechecked_after_enrollment()
		{
			var entry = await _outbox.EnqueueAsync(42, "Checklists", Event());
			entry.PayloadJson.Should().NotContain("CANARY").And.NotContain("Data").And.NotContain("person-42");
			JObject.Parse(entry.PayloadJson)["Score"].Value<decimal>().Should().Be(87.25m);
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			DomainEventDispatchedEvent delivered = null;
			_bus.AddAsyncListener<DomainEventDispatchedEvent>(e => { delivered = e; return Task.CompletedTask; });
			(await _outbox.DispatchAfterCommitAsync(new[] { entry.DomainEventOutboxId })).Should().Be(1);
			var payload = JObject.Parse(delivered.PayloadJson);
			payload["Score"].Value<string>().Should().Be("REDACTED"); payload["Passed"].Value<string>().Should().Be("REDACTED"); payload["TargetId"].Value<string>().Should().Be("REDACTED");
			payload["is_redacted"].Value<bool>().Should().BeTrue(); payload["catalog_version"].Value<int>().Should().Be(18);
			entry.PayloadJson.Should().StartWith("rgdp:").And.NotContain("87.25").And.NotContain("person-42");
			_history.Decrypt(42, "domaineventoutbox.payloadjson", entry.DomainEventOutboxId.ToString(), entry.PayloadJson).Should().Contain("87.25").And.NotContain("person-42");
		}
		[Test]
		public async Task Protected_or_unknown_policy_never_persists_plaintext_outcomes()
		{
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ThrowsAsync(new InvalidOperationException("policy unavailable"));
			Func<Task> enqueue = () => _outbox.EnqueueAsync(42, "Checklists", Event());
			await enqueue.Should().ThrowAsync<InvalidOperationException>();
			_store.OutboxRepo.Verify(s => s.InitializeChecklistPayloadAsync(It.IsAny<DomainEventOutboxEntry>(), It.IsAny<CancellationToken>()), Times.Never);
		}
		[Test]
		public async Task Async_subscriber_is_awaited_and_failure_remains_retryable_without_logging_its_message()
		{
			var entry = await _outbox.EnqueueAsync(42, "Checklists", Event());
			var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var listener = _bus.AddAsyncListener<DomainEventDispatchedEvent>(e => completion.Task);
			var dispatch = _outbox.DispatchAfterCommitAsync(new[] { entry.DomainEventOutboxId });
			dispatch.IsCompleted.Should().BeFalse(); entry.DispatchedOn.Should().BeNull();
			completion.SetException(new InvalidOperationException("SYNTHETIC-PHI-CANARY"));
			(await dispatch).Should().Be(0); entry.LastError.Should().NotContain("CANARY"); entry.State.Should().Be((int)DomainEventOutboxState.Pending);
			_bus.RemoveListener(listener); string deliveredId = null;
			_bus.AddAsyncListener<DomainEventDispatchedEvent>(e => { deliveredId = e.EventId; e.IsReplay.Should().BeTrue(); return Task.CompletedTask; });
			(await _outbox.DispatchAfterCommitAsync(new[] { entry.DomainEventOutboxId })).Should().Be(1); deliveredId.Should().Be(entry.EventId);
		}

		[Test]
		public async Task Already_encrypted_historical_events_replay_from_safe_routing_without_decrypting_history()
		{
			var entry = await _outbox.EnqueueAsync(42, "Checklists", Event());
			entry.PayloadJson = JObject.FromObject(Event().Payload).ToString();
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			await _history.Service.ProtectAsync(42, entry.DomainEventOutboxId.ToString(), entry, ReadinessHistoryFields.Outbox);
			var encrypted = entry.PayloadJson;
			string delivered = null;
			_bus.AddAsyncListener<DomainEventDispatchedEvent>(e => { delivered = e.PayloadJson; return Task.CompletedTask; });
			(await _outbox.DispatchAfterCommitAsync(new[] { entry.DomainEventOutboxId })).Should().Be(1);
			delivered.Should().NotContain("CANARY").And.NotContain("87.25").And.NotContain("person-42").And.NotContain("rgdp:");
			entry.PayloadJson.Should().Be(encrypted);
			_history.Decrypt(42, "domaineventoutbox.payloadjson", entry.DomainEventOutboxId.ToString(), entry.PayloadJson).Should().Contain("SYNTHETIC-PHI-CANARY");
			_history.Broker.Invocations.Should().OnlyContain(i => i.Method.Name == "EncryptAsync");
		}
		[TestCase(67), TestCase(69), TestCase(164), TestCase(165)]
		public async Task Queue_rejection_after_run_insert_retries_the_same_workflow_run(int trigger)
		{
			var workflows = new Mock<IWorkflowRepository>(); var runs = new Mock<IWorkflowRunRepository>(); var queue = new Mock<IOutboundQueueProvider>();
			var subscriptions = new Mock<ISubscriptionsService>(); var departments = new Mock<IDepartmentsService>();
			subscriptions.Setup(s => s.GetCurrentPlanForDepartmentAsync(42, It.IsAny<bool>())).ReturnsAsync(new Plan { PlanId = 999999 });
			var workflow = new Workflow { WorkflowId = Guid.NewGuid().ToString(), DepartmentId = 42, TriggerEventType = trigger };
			workflows.Setup(s => s.GetAllActiveByDepartmentAndEventTypeAsync(42, trigger)).ReturnsAsync(new[] { workflow });
			WorkflowRun stored = null;
			runs.Setup(s => s.GetByWorkflowsAndEventAsync(42, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>())).ReturnsAsync(() => stored == null ? new List<WorkflowRun>() : new List<WorkflowRun> { stored });
			runs.Setup(s => s.InsertAsync(It.IsAny<WorkflowRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((WorkflowRun r, CancellationToken ct, bool first) => stored = r);
			var attempts = new List<WorkflowQueueItem>();
			queue.Setup(s => s.EnqueueWorkflow(It.IsAny<WorkflowQueueItem>())).ReturnsAsync((WorkflowQueueItem item) => { attempts.Add(item); return attempts.Count > 1; });
			_ = new WorkflowEventProvider(_bus, queue.Object, workflows.Object, runs.Object, departments.Object, subscriptions.Object, _projection, _history.Lazy);
			var envelope = Event(); envelope.Trigger = (WorkflowTriggerEventType)trigger; envelope.EventName = envelope.Trigger.ToString();
			var entry = await _outbox.EnqueueAsync(42, "Checklists", envelope);
			(await _outbox.DispatchAfterCommitAsync(new[] { entry.DomainEventOutboxId })).Should().Be(0); stored.Should().NotBeNull();
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			(await _outbox.DispatchAfterCommitAsync(new[] { entry.DomainEventOutboxId })).Should().Be(1);
			attempts.Should().HaveCount(2); attempts[1].WorkflowRunId.Should().Be(attempts[0].WorkflowRunId);
			attempts[1].EventPayloadJson.Should().NotContain("87.25").And.NotContain("CANARY");
			runs.Verify(s => s.InsertAsync(It.IsAny<WorkflowRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}
		[TestCase(69), TestCase(164), TestCase(165)]
		public async Task Scheduling_event_replay_preserves_safe_timing_and_designer_context_without_decrypting(int trigger)
		{
			var schedule = Guid.NewGuid().ToString(); var occurrence = Guid.NewGuid().ToString(); var time = new DateTimeOffset(2026, 9, 8, 8, 0, 0, TimeSpan.FromHours(2));
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			var entry = await _outbox.EnqueueAsync(42, "Checklists", new DomainEventEnvelope { Trigger = (WorkflowTriggerEventType)trigger, EventName = ((WorkflowTriggerEventType)trigger).ToString(), AggregateType = "ChecklistOccurrence", AggregateId = occurrence,
				Payload = new { ScheduleId = schedule, OccurrenceId = occurrence, PeriodStartUtc = time, WindowEndUtc = time.AddHours(1), Revision = 4, State = 5, IsActive = true, TargetType = 3, TargetId = "SYNTHETIC-PII-CANARY", Reason = "SYNTHETIC-PHI-CANARY" } });
			entry.PayloadJson.Should().StartWith("rgdp:");
			string delivered = null; _bus.AddAsyncListener<DomainEventDispatchedEvent>(e => { delivered = e.PayloadJson; return Task.CompletedTask; });
			(await _outbox.DispatchAfterCommitAsync(new[] { entry.DomainEventOutboxId })).Should().Be(1);
			delivered.Should().NotContain("CANARY").And.NotContain("rgdp:");
			var payload = JObject.Parse(delivered); payload["ScheduleId"].Value<string>().Should().Be(schedule); payload["PeriodStartUtc"].Value<DateTime>().Should().Be(time.UtcDateTime);
			var builder = new WorkflowTemplateContextBuilder(Mock.Of<IDepartmentsService>(), Mock.Of<IDepartmentSettingsService>(), Mock.Of<IUserProfileService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(), Mock.Of<IUnitsService>(), Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IDepartmentProfileMediaService>());
			var context = (ScriptObject)await builder.BuildContextAsync(42, (WorkflowTriggerEventType)trigger, new JObject { ["Payload"] = payload }.ToString(), CancellationToken.None);
			Template.Parse("{{ checklist.schedule_id }}|{{ checklist.revision }}|{{ checklist.is_active }}").Render(context).Should().Be(schedule + "|4|true");
			((ScriptObject)context["checklist"])["url"].ToString().Should().EndWith("/User/Checklists/Due");
			var sample = (ScriptObject)((ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData((WorkflowTriggerEventType)trigger))["checklist"];
			foreach (var key in new[] { "schedule_id", "occurrence_id", "period_start_utc", "window_end_utc", "revision", "state", "is_active" }) sample.Should().ContainKey(key);
			_history.Broker.Invocations.Should().OnlyContain(i => i.Method.Name == "EncryptAsync");
		}
		[Test]
		public async Task Queued_payloads_and_templates_preserve_redaction_after_policy_changes()
		{
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			var queued = new JObject { ["Payload"] = JObject.FromObject(Event().Payload), ["LegacyNote"] = "SYNTHETIC-PHI-CANARY" };
			var safe = await ChecklistWorkflowPayload.ProjectAsync(42, queued, _projection, wrapped: true);
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(false);
			safe = await ChecklistWorkflowPayload.ProjectAsync(42, JObject.Parse(safe), _projection, wrapped: true);
			safe.Should().NotContain("CANARY").And.NotContain("87.25");
			var builder = new WorkflowTemplateContextBuilder(Mock.Of<IDepartmentsService>(), Mock.Of<IDepartmentSettingsService>(), Mock.Of<IUserProfileService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(), Mock.Of<IUnitsService>(), Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IDepartmentProfileMediaService>());
			var context = (ScriptObject)await builder.BuildContextAsync(42, WorkflowTriggerEventType.ChecklistCompleted, safe, CancellationToken.None);
			var text = Template.Parse("{{ checklist.score }}|{{ checklist.passed }}|{{ protection.is_redacted }}").Render(context);
			text.Should().Be("REDACTED|REDACTED|true");
		}
		[Test]
		public async Task Execution_rechecks_a_legacy_queue_payload_and_does_not_repeat_a_completed_run()
		{
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			var workflows = new Mock<IWorkflowRepository>(); var runs = new Mock<IWorkflowRunRepository>(); var context = new Mock<IWorkflowTemplateContextBuilder>();
			var workflow = new Workflow { WorkflowId = Guid.NewGuid().ToString(), DepartmentId = 42, TriggerEventType = 67 };
			var run = new WorkflowRun { WorkflowRunId = Guid.NewGuid().ToString(), WorkflowId = workflow.WorkflowId, DepartmentId = 42, Status = (int)WorkflowRunStatus.Completed };
			workflows.Setup(s => s.GetByIdAsync(workflow.WorkflowId)).ReturnsAsync(workflow);
			runs.Setup(s => s.GetByIdAsync(run.WorkflowRunId)).ReturnsAsync(run);
			string claimedPayload = null;
			runs.Setup(s => s.TryStartChecklistRunAsync(run.WorkflowRunId, workflow.WorkflowId, 42, 1, It.IsAny<string>())).ReturnsAsync((string r, string w, int d, int a, string p) => { claimedPayload = p; return false; });
			var service = new WorkflowService(workflows.Object, Mock.Of<IWorkflowStepRepository>(), Mock.Of<IWorkflowCredentialRepository>(), runs.Object, Mock.Of<IWorkflowRunLogRepository>(), Mock.Of<IWorkflowDailyUsageRepository>(), Mock.Of<IEncryptionService>(), Mock.Of<IWorkflowActionExecutorFactory>(), context.Object, Mock.Of<ISubscriptionsService>(), Mock.Of<IRecordsExportService>(), new Lazy<IProtectedProjectionService>(() => _projection), _history.Lazy);
			(await service.ExecuteWorkflowAsync(workflow.WorkflowId, JsonConvert.SerializeObject(new { Payload = Event().Payload }), 42, "", existingRunId: run.WorkflowRunId)).Should().BeSameAs(run);
			claimedPayload.Should().StartWith("rgdp:");
            _history.Decrypt(42, "workflowruns.inputpayload", run.WorkflowRunId, claimedPayload).Should().NotContain("CANARY").And.NotContain("87.25").And.Contain("REDACTED");
			context.Verify(s => s.BuildContextAsync(It.IsAny<int>(), It.IsAny<WorkflowTriggerEventType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Execution_seals_rendered_output_and_provider_errors_and_redacts_workflow_history()
		{
			_policy.Setup(s => s.IsProtectionEnforcedAsync(42)).ReturnsAsync(true);
			var workflows = new Mock<IWorkflowRepository>(); var runs = new Mock<IWorkflowRunRepository>(); var logs = new Mock<IWorkflowRunLogRepository>(); var steps = new Mock<IWorkflowStepRepository>(); var context = new Mock<IWorkflowTemplateContextBuilder>();
			var workflow = new Workflow { WorkflowId = Guid.NewGuid().ToString(), DepartmentId = 42, TriggerEventType = 67, MaxRetryCount = 1 };
			var run = new WorkflowRun { WorkflowRunId = Guid.NewGuid().ToString(), WorkflowId = workflow.WorkflowId, DepartmentId = 42, TriggerEventType = 67, Status = (int)WorkflowRunStatus.Pending };
			var step = new WorkflowStep { WorkflowStepId = Guid.NewGuid().ToString(), WorkflowId = workflow.WorkflowId, IsEnabled = true, ActionType = (int)WorkflowActionType.SendEmail, OutputTemplate = "SYNTHETIC-TEMPLATE-CANARY" };
			workflows.Setup(s => s.GetByIdAsync(workflow.WorkflowId)).ReturnsAsync(workflow);
			runs.Setup(s => s.GetByIdAsync(run.WorkflowRunId)).ReturnsAsync(run);
			runs.Setup(s => s.GetRunsByWorkflowIdAsync(workflow.WorkflowId, It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new[] { run });
			runs.Setup(s => s.TryStartChecklistRunAsync(run.WorkflowRunId, workflow.WorkflowId, 42, 1, It.IsAny<string>())).ReturnsAsync(true);
			steps.Setup(s => s.GetAllByWorkflowIdAsync(workflow.WorkflowId)).ReturnsAsync(new[] { step });
			context.Setup(s => s.BuildContextAsync(42, WorkflowTriggerEventType.ChecklistCompleted, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ScriptObject());
			var executor = new Mock<IWorkflowActionExecutor>(); var factory = new Mock<IWorkflowActionExecutorFactory>();
			factory.Setup(s => s.GetExecutor(WorkflowActionType.SendEmail)).Returns(executor.Object);
			executor.Setup(s => s.ExecuteAsync(It.IsAny<WorkflowActionContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WorkflowActionResult { Success = false, ResultMessage = "SYNTHETIC-RESULT-CANARY", ErrorDetail = "SYNTHETIC-ERROR-CANARY" });
			WorkflowRunLog storedLog = null;
			logs.Setup(s => s.InsertAsync(It.IsAny<WorkflowRunLog>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((WorkflowRunLog l, CancellationToken ct, bool first) => storedLog = l);
			logs.Setup(s => s.GetByWorkflowRunIdAsync(run.WorkflowRunId)).ReturnsAsync(() => new[] { storedLog });
			var service = new WorkflowService(workflows.Object, steps.Object, Mock.Of<IWorkflowCredentialRepository>(), runs.Object, logs.Object, Mock.Of<IWorkflowDailyUsageRepository>(), Mock.Of<IEncryptionService>(), factory.Object, context.Object, Mock.Of<ISubscriptionsService>(), Mock.Of<IRecordsExportService>(), new Lazy<IProtectedProjectionService>(() => _projection), _history.Lazy);
			await service.ExecuteWorkflowAsync(workflow.WorkflowId, JsonConvert.SerializeObject(new { Payload = Event().Payload }), 42, "", existingRunId: run.WorkflowRunId);
			storedLog.Should().NotBeNull(); storedLog.RenderedOutput.Should().StartWith("rgdp:"); storedLog.ErrorMessage.Should().StartWith("rgdp:"); storedLog.ActionResult.Should().StartWith("rgdp:");
			_history.Decrypt(42, "workflowrunlogs.renderedoutput", storedLog.WorkflowRunLogId, storedLog.RenderedOutput).Should().Be("SYNTHETIC-TEMPLATE-CANARY");
			_history.Decrypt(42, "workflowrunlogs.errormessage", storedLog.WorkflowRunLogId, storedLog.ErrorMessage).Should().Be("SYNTHETIC-ERROR-CANARY");
			(await service.GetWorkflowRunByIdAsync(run.WorkflowRunId)).InputPayload.Should().Be("REDACTED");
			(await service.GetLogsForRunAsync(run.WorkflowRunId)).Single().RenderedOutput.Should().Be("REDACTED");
			(await service.GetWorkflowHealthAsync(workflow.WorkflowId)).LastErrorMessage.Should().Be("REDACTED");
			run.InputPayload.Should().StartWith("rgdp:"); storedLog.RenderedOutput.Should().StartWith("rgdp:");
		}
	}
}
