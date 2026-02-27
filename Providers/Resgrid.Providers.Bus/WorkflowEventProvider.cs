using System;
using System.Linq;
using System.Threading;
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
			ISubscriptionsService subscriptionsService)
		{
			_eventAggregator        = eventAggregator;
			_outboundQueueProvider  = outboundQueueProvider;
			_workflowRepository     = workflowRepository;
			_runRepository          = runRepository;
			_departmentsService     = departmentsService;
			_subscriptionsService   = subscriptionsService;

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
		}

		private static async void HandleEvent(int departmentId, WorkflowTriggerEventType eventType, object eventObj)
		{
			try
			{
				// ── Plan-aware rate limiting ─────────────────────────────────────────
				var plan     = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId);
				var isFreePlan = plan?.IsFree ?? false;

				if (isFreePlan)
				{
					// Free plan: aggressive per-minute limit with NO event-type exemptions
					if (!IsWithinRateLimit(departmentId, WorkflowConfig.FreePlanRateLimitPerDepartmentPerMinute))
						return;

					// Free plan: daily run cap
					if (!IsWithinDailyLimit(departmentId, WorkflowConfig.FreePlanDailyRunLimit))
						return;
				}
				else
				{
					// Paid plan: standard limit; call/update/close events are exempt
					if (!_rateLimitExemptEventTypes.Contains(eventType) &&
					    !IsWithinRateLimit(departmentId, WorkflowConfig.RateLimitPerDepartmentPerMinute))
						return;
				}
				// ── End rate limiting ────────────────────────────────────────────────

				var workflows = await _workflowRepository.GetAllActiveByDepartmentAndEventTypeAsync(
					departmentId, (int)eventType);

				if (workflows == null) return;

				var payloadJson = JsonConvert.SerializeObject(eventObj);
				var department  = await _departmentsService.GetDepartmentByIdAsync(departmentId);
				var deptCode    = department?.Code ?? string.Empty;

				foreach (var workflow in workflows)
				{
					var run = new WorkflowRun
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
					};
					run = await _runRepository.InsertAsync(run, CancellationToken.None);

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

