using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Providers.Bus
{
	/// <summary>
	/// Subscribes to all domain events and, for each active workflow whose trigger matches,
	/// creates a WorkflowRun (Pending) and enqueues a WorkflowQueueItem to RabbitMQ.
	/// Free-plan departments are subject to an aggressive, non-bypassable rate limit.
	/// </summary>
	public class WorkflowEventProvider : IWorkflowEventProvider
	{
		private readonly IEventAggregator _eventAggregator;
		private static IOutboundQueueProvider _outboundQueueProvider;
		private static IWorkflowRepository _workflowRepository;
		private static IWorkflowRunRepository _runRepository;
		private static IDepartmentsService _departmentsService;
		private static ISubscriptionsService _subscriptionsService;
		private static IProtectedProjectionService _protectedProjectionService;

		// Per-minute rate limit tracker: departmentId → (window start, count)
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (DateTime Window, int Count)> _rateLimitTracker
			= new System.Collections.Concurrent.ConcurrentDictionary<int, (DateTime, int)>();

		// Daily run tracker for free-plan departments: departmentId → (UTC date, count)
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (DateTime Date, int Count)> _dailyRunTracker
			= new System.Collections.Concurrent.ConcurrentDictionary<int, (DateTime, int)>();

		/// <summary>
		/// Event types exempt from the standard per-minute rate limit for PAID plans only.
		/// Free-plan departments are NEVER exempt — all event types count against their limit.
		/// </summary>
		private static readonly System.Collections.Generic.HashSet<WorkflowTriggerEventType> _rateLimitExemptEventTypes
			= new System.Collections.Generic.HashSet<WorkflowTriggerEventType>
			{
				WorkflowTriggerEventType.CallAdded,
				WorkflowTriggerEventType.CallUpdated,
				WorkflowTriggerEventType.CallClosed
			};

		public WorkflowEventProvider(
			IEventAggregator eventAggregator,
			IOutboundQueueProvider outboundQueueProvider,
			IWorkflowRepository workflowRepository,
			IWorkflowRunRepository runRepository,
			IDepartmentsService departmentsService,
			ISubscriptionsService subscriptionsService,
			IProtectedProjectionService protectedProjectionService)
		{
			_eventAggregator        = eventAggregator;
			_outboundQueueProvider  = outboundQueueProvider;
			_workflowRepository     = workflowRepository;
			_runRepository          = runRepository;
			_departmentsService     = departmentsService;
			_subscriptionsService   = subscriptionsService;
			_protectedProjectionService = protectedProjectionService;

			RegisterListeners();
		}

		private void RegisterListeners()
		{
			_eventAggregator.AddListener<CallAddedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.CallAdded, e));
			_eventAggregator.AddListener<CallUpdatedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.CallUpdated, e));
			_eventAggregator.AddListener<CallClosedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.CallClosed, e));
			_eventAggregator.AddListener<UnitStatusEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.UnitStatusChanged, e));
			_eventAggregator.AddListener<UserStaffingEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.PersonnelStaffingChanged, e));
			_eventAggregator.AddListener<UserStatusEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.PersonnelStatusChanged, e));
			_eventAggregator.AddListener<UserCreatedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.UserCreated, e));
			_eventAggregator.AddListener<UserAssignedToGroupEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.UserAssignedToGroup, e));
			_eventAggregator.AddListener<DocumentAddedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.DocumentAdded, e));
			_eventAggregator.AddListener<NoteAddedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.NoteAdded, e));
			_eventAggregator.AddListener<UnitAddedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.UnitAdded, e));
			_eventAggregator.AddListener<LogAddedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.LogAdded, e));
			_eventAggregator.AddListener<CalendarEventAddedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.CalendarEventAdded, e));
			_eventAggregator.AddListener<CalendarEventUpdatedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.CalendarEventUpdated, e));
			_eventAggregator.AddListener<ShiftCreatedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.ShiftCreated, e));
			_eventAggregator.AddListener<ShiftUpdatedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.ShiftUpdated, e));
			_eventAggregator.AddListener<ResourceOrderAddedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.ResourceOrderAdded, e));
			_eventAggregator.AddListener<ShiftTradeRequestedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.ShiftTradeRequested, e));
			_eventAggregator.AddListener<ShiftTradeFilledEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.ShiftTradeFilled, e));
			_eventAggregator.AddListener<MessageSentEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.MessageSent, e));
			_eventAggregator.AddListener<TrainingAddedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.TrainingAdded, e));
			_eventAggregator.AddListener<TrainingUpdatedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.TrainingUpdated, e));
			_eventAggregator.AddListener<InventoryAdjustedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.InventoryAdjusted, e));
			_eventAggregator.AddListener<CertificationExpiringEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.CertificationExpiring, e));
			_eventAggregator.AddListener<FormSubmittedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.FormSubmitted, e));
			_eventAggregator.AddListener<PersonnelRoleChangedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.PersonnelRoleChanged, e));
			_eventAggregator.AddListener<GroupAddedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.GroupAdded, e));
			_eventAggregator.AddListener<GroupUpdatedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.GroupUpdated, e));

			// Incident Command (§3.12)
			_eventAggregator.AddListener<CommandEstablishedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.CommandEstablished, e));
			_eventAggregator.AddListener<CommandTransferredEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.CommandTransferred, e));
			_eventAggregator.AddListener<IncidentObjectiveCompletedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.ObjectiveCompleted, e));
			_eventAggregator.AddListener<IncidentClosedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.IncidentClosed, e));
			_eventAggregator.AddListener<IncidentResourceAssignedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.ResourceAssigned, e));
			_eventAggregator.AddListener<IncidentResourceReleasedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.ResourceReleased, e));
			_eventAggregator.AddListener<IncidentRoleAssignedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.IncidentRoleAssigned, e));
			_eventAggregator.AddListener<AdHocResourceCreatedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.AdHocResourceCreated, e));
			_eventAggregator.AddListener<IncidentChannelOpenedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.IncidentChannelOpened, e));
			_eventAggregator.AddListener<CriticalParDetectedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.CriticalParDetected, e));
			_eventAggregator.AddListener<IncidentNoteAddedEvent>(e => HandleEvent(e.DepartmentId,
				e.Visibility == (int)IncidentContentVisibility.Public ? WorkflowTriggerEventType.PublicIncidentNoteAdded : WorkflowTriggerEventType.InternalIncidentNoteAdded, e));
			_eventAggregator.AddListener<IncidentAttachmentAddedEvent>(e => HandleEvent(e.DepartmentId,
				e.Visibility == (int)IncidentContentVisibility.Public ? WorkflowTriggerEventType.PublicIncidentDocumentAdded : WorkflowTriggerEventType.InternalIncidentDocumentAdded, e));
			_eventAggregator.AddListener<IncidentNoteRemovedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.IncidentNoteRemoved, e));
			_eventAggregator.AddListener<IncidentAttachmentRemovedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.IncidentDocumentRemoved, e));
			_eventAggregator.AddListener<IncidentActionPlanUpdatedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.IncidentActionPlanUpdated, e));
			_eventAggregator.AddListener<IncidentCommandPostUpdatedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.IncidentCommandPostUpdated, e));
			_eventAggregator.AddListener<IncidentPublicSharingChangedEvent>(e => HandleEvent(e.DepartmentId,
				e.Enabled ? WorkflowTriggerEventType.IncidentPublicSharingEnabled : WorkflowTriggerEventType.IncidentPublicSharingDisabled, e));

			// Run card dispatch system
			_eventAggregator.AddListener<RunCardActivatedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.RunCardActivated, e));
			_eventAggregator.AddListener<CallAlarmEscalatedEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.CallAlarmEscalated, e));
			_eventAggregator.AddListener<DispatchShortfallEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.DispatchShortfallDetected, e));
			_eventAggregator.AddListener<StationCoverageGapEvent>(e => HandleEvent(e.DepartmentId, WorkflowTriggerEventType.StationCoverageGapDetected, e));

			// Records (RMS) lifecycle triggers 100-107 arrive through the DomainEventOutbox dispatcher (post-commit in
			// the producing process, or worker command 40's sweep), never straight from the Records transaction
			// (plan section 5.6). The legacy LogAdded compatibility projection rides the same road with
			// TriggerEventType = LogAdded and a LogAddedEvent-shaped payload, so existing LogAdded workflows keep
			// their exact log.* contract without a legacy Logs row ever being written.
			_eventAggregator.AddListener<DomainEventDispatchedEvent>(e => HandleDomainEvent(e));
		}

		private static void HandleDomainEvent(DomainEventDispatchedEvent dispatched)
		{
			if (dispatched == null || !dispatched.TriggerEventType.HasValue ||
				!Enum.IsDefined(typeof(WorkflowTriggerEventType), dispatched.TriggerEventType.Value))
				return;

			// Replays (activation, legacy indexing, operator re-drives) must never flood workflows.
			if (dispatched.IsReplay)
				return;

			var trigger = (WorkflowTriggerEventType)dispatched.TriggerEventType.Value;
			var evt = RecordsWorkflowEvent.From(dispatched);

			if (trigger == WorkflowTriggerEventType.LogAdded)
			{
				LogAddedEvent compatibility;
				try
				{
					compatibility = evt.Payload?.ToObject<LogAddedEvent>();
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"LogAdded compatibility payload for event {dispatched.EventId} could not be read.");
					return;
				}

				if (compatibility?.Log == null)
					return;

				compatibility.DepartmentId = dispatched.DepartmentId;
				HandleEvent(dispatched.DepartmentId, trigger, compatibility, dispatched);
				return;
			}

			HandleEvent(dispatched.DepartmentId, trigger, evt, dispatched);
		}

		/// <summary>
		/// Creates one Pending run per matching workflow and enqueues it. For Records events (an envelope is
		/// present) the plan section 5.6 contract applies on top: the run carries the event envelope, one initial
		/// run exists per (WorkflowId, EventId), and a rate- or daily-limited event is persisted as a Skipped run
		/// with its reason rather than dropped. Legacy in-process events keep today's behaviour.
		/// </summary>
		private static async void HandleEvent(int departmentId, WorkflowTriggerEventType eventType, object eventObj, DomainEventDispatchedEvent envelope = null)
		{
			try
			{
				System.Collections.Generic.List<Workflow> workflows = null;
				string payloadJson = null;

				if (envelope != null)
				{
					// The skip rows below need the workflow list, so for Records events it is loaded before the limits.
					workflows = (await _workflowRepository.GetAllActiveByDepartmentAndEventTypeAsync(departmentId, (int)eventType))?.ToList();
					if (workflows == null || workflows.Count == 0)
						return;

					payloadJson = await _protectedProjectionService.BuildSafeWorkflowPayloadAsync(departmentId, eventObj);
				}

				// ── Plan-aware rate limiting ─────────────────────────────────────────
				var plan     = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId);
				var isFreePlan = plan?.IsFree ?? false;

				if (isFreePlan)
				{
					// Free plan: aggressive per-minute limit with NO event-type exemptions
					if (!IsWithinRateLimit(departmentId, WorkflowConfig.FreePlanRateLimitPerDepartmentPerMinute))
					{
						await RecordSkippedAsync(workflows, departmentId, eventType, envelope, payloadJson, WorkflowRunSkipReasons.RateLimit);
						return;
					}

					// Free plan: daily run cap
					if (!IsWithinDailyLimit(departmentId, WorkflowConfig.FreePlanDailyRunLimit))
					{
						await RecordSkippedAsync(workflows, departmentId, eventType, envelope, payloadJson, WorkflowRunSkipReasons.DailyLimit);
						return;
					}
				}
				else
				{
					// Paid plan: standard limit; call/update/close events are exempt
					if (!_rateLimitExemptEventTypes.Contains(eventType) &&
					    !IsWithinRateLimit(departmentId, WorkflowConfig.RateLimitPerDepartmentPerMinute))
					{
						await RecordSkippedAsync(workflows, departmentId, eventType, envelope, payloadJson, WorkflowRunSkipReasons.RateLimit);
						return;
					}
				}
				// ── End rate limiting ────────────────────────────────────────────────

				if (workflows == null)
					workflows = (await _workflowRepository.GetAllActiveByDepartmentAndEventTypeAsync(departmentId, (int)eventType))?.ToList();

				if (workflows == null || workflows.Count == 0) return;

				// ADP safe projection (plan section 8): for protected departments the payload is
				// redacted HERE, before it reaches WorkflowRun.InputPayload, the queue, retries,
				// dead letters, history, or designer previews.
				if (payloadJson == null)
					payloadJson = await _protectedProjectionService.BuildSafeWorkflowPayloadAsync(departmentId, eventObj);
				var department  = await _departmentsService.GetDepartmentByIdAsync(departmentId);
				var deptCode    = department?.Code ?? string.Empty;

				foreach (var workflow in workflows)
				{
					if (await IsDuplicateAsync(workflow.WorkflowId, envelope))
						continue;

					var run = WorkflowRunEnvelope.Apply(new WorkflowRun
					{
						WorkflowRunId    = Guid.NewGuid().ToString(),
						WorkflowId       = workflow.WorkflowId,
						DepartmentId     = departmentId,
						Status           = (int)WorkflowRunStatus.Pending,
						TriggerEventType = (int)eventType,
						InputPayload     = payloadJson,
						StartedOn        = DateTime.UtcNow,
						QueuedOn         = DateTime.UtcNow,
						AttemptNumber    = 1
					}, envelope);

					try
					{
						run = await _runRepository.InsertAsync(run, CancellationToken.None);
					}
					catch (Exception ex) when (envelope != null && WorkflowRunEnvelope.IsDuplicateKeyViolation(ex))
					{
						// Two dispatchers (post-commit and the worker sweep) raced on the same event; the index is the backstop.
						Framework.Logging.LogInfo($"Workflow {workflow.WorkflowId} already has a run for event {envelope.EventId}; duplicate dispatch ignored.");
						continue;
					}

					var queueItem = new WorkflowQueueItem
					{
						WorkflowId       = workflow.WorkflowId,
						WorkflowRunId    = run.WorkflowRunId,
						DepartmentId     = departmentId,
						DepartmentCode   = deptCode,
						TriggerEventType = (int)eventType,
						EventPayloadJson = payloadJson,
						AttemptNumber    = 1,
						EnqueuedOn       = DateTime.UtcNow
					};

					await _outboundQueueProvider.EnqueueWorkflow(queueItem);

					// Increment free-plan daily counter after each successful enqueue
					if (isFreePlan)
						IncrementDailyCount(departmentId);
				}
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}
		}

		/// <summary>One initial run per (WorkflowId, EventId): a retry or a second dispatcher reuses the existing run.</summary>
		private static async Task<bool> IsDuplicateAsync(string workflowId, DomainEventDispatchedEvent envelope)
		{
			if (envelope == null || string.IsNullOrWhiteSpace(envelope.EventId))
				return false;

			var existing = await _runRepository.GetByWorkflowAndEventAsync(workflowId, envelope.EventId);
			if (existing == null)
				return false;

			Framework.Logging.LogInfo($"Workflow {workflowId} already has run {existing.WorkflowRunId} for event {envelope.EventId}; duplicate dispatch ignored.");
			return true;
		}

		/// <summary>
		/// A Records event that a plan or rate limit suppresses is persisted as a Skipped run per workflow, with the
		/// reason, so it shows in run history and health instead of vanishing (plan section 5.6). Legacy events
		/// (no envelope) are still dropped silently, as before.
		/// </summary>
		private static async Task RecordSkippedAsync(System.Collections.Generic.List<Workflow> workflows, int departmentId, WorkflowTriggerEventType eventType,
			DomainEventDispatchedEvent envelope, string payloadJson, string reason)
		{
			if (envelope == null || workflows == null)
				return;

			var now = DateTime.UtcNow;
			foreach (var workflow in workflows)
			{
				if (await IsDuplicateAsync(workflow.WorkflowId, envelope))
					continue;

				var run = WorkflowRunEnvelope.MarkSkipped(WorkflowRunEnvelope.Apply(new WorkflowRun
				{
					WorkflowRunId    = Guid.NewGuid().ToString(),
					WorkflowId       = workflow.WorkflowId,
					DepartmentId     = departmentId,
					TriggerEventType = (int)eventType,
					InputPayload     = payloadJson,
					StartedOn        = now,
					QueuedOn         = now,
					AttemptNumber    = 1
				}, envelope), reason, now);

				try
				{
					await _runRepository.InsertAsync(run, CancellationToken.None);
					Framework.Logging.LogError($"Records event {envelope.EventId} ({eventType}) was skipped for workflow {workflow.WorkflowId} in department {departmentId}: {reason}.");
				}
				catch (Exception ex) when (WorkflowRunEnvelope.IsDuplicateKeyViolation(ex))
				{
					// Already recorded by the other dispatcher.
				}
			}
		}

		private static bool IsWithinRateLimit(int departmentId, int limit)
		{
			var now = DateTime.UtcNow;

			_rateLimitTracker.AddOrUpdate(
				departmentId,
				_ => (now, 1),
				(_, existing) =>
				{
					if ((now - existing.Window).TotalMinutes >= 1)
						return (now, 1);
					return (existing.Window, existing.Count + 1);
				});

			var (_, count) = _rateLimitTracker[departmentId];
			return count <= limit;
		}

		private static bool IsWithinDailyLimit(int departmentId, int dailyLimit)
		{
			var today = DateTime.UtcNow.Date;

			_dailyRunTracker.AddOrUpdate(
				departmentId,
				_ => (today, 0),  // 0 — will be incremented after successful enqueue
				(_, existing) =>
				{
					// Reset counter when the UTC date rolls over
					if (existing.Date != today)
						return (today, 0);
					return existing;
				});

			var (_, count) = _dailyRunTracker[departmentId];
			return count < dailyLimit;
		}

		private static void IncrementDailyCount(int departmentId)
		{
			var today = DateTime.UtcNow.Date;
			_dailyRunTracker.AddOrUpdate(
				departmentId,
				_ => (today, 1),
				(_, existing) =>
				{
					if (existing.Date != today) return (today, 1);
					return (existing.Date, existing.Count + 1);
				});
		}
	}
}

