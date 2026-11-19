using System;

namespace Resgrid.Model.Events
{
	/// <summary>What happened to a recurring call schedule (<see cref="RecurringCallScheduleEvent"/>).</summary>
	public enum RecurringCallScheduleChanges
	{
		Created = 1,
		Updated = 2,
		Paused = 3,
		Resumed = 4,
		Deleted = 5
	}

	/// <summary>
	/// A recurring call schedule was created, changed, paused, resumed or deleted. Workflow triggers
	/// <see cref="WorkflowTriggerEventType.RecurringCallScheduleCreated"/> to <see cref="WorkflowTriggerEventType.RecurringCallScheduleDeleted"/>.
	/// </summary>
	public class RecurringCallScheduleEvent
	{
		public int DepartmentId { get; set; }
		public RecurringCallScheduleChanges Change { get; set; }
		public int RecurringCallId { get; set; }
		public string Name { get; set; }
		public string NatureOfCall { get; set; }
		public string Address { get; set; }
		public string FacilityName { get; set; }
		public string DestinationName { get; set; }
		public string ScheduleContactName { get; set; }
		public string ScheduleContactNumber { get; set; }
		/// <summary>Department-local dates.</summary>
		public DateTime StartsOn { get; set; }
		public DateTime? EndsOn { get; set; }
		/// <summary>"HH:mm" local times, comma-separated.</summary>
		public string Times { get; set; }
		/// <summary><see cref="RecurringCallCreationModes"/>.</summary>
		public int CreationMode { get; set; }
		public bool UseRunCard { get; set; }
		public bool IsActive { get; set; }
		public string PauseReason { get; set; }
		public DateTime? ResumeOn { get; set; }
		/// <summary>Who made the change; null when the system did (an automatic resume).</summary>
		public string UserId { get; set; }
		/// <summary>The next occurrence still to come, UTC; null when none (paused, ended or deleted).</summary>
		public DateTime? NextOccurrenceOn { get; set; }

		public WorkflowTriggerEventType Trigger
		{
			get
			{
				switch (Change)
				{
					case RecurringCallScheduleChanges.Created: return WorkflowTriggerEventType.RecurringCallScheduleCreated;
					case RecurringCallScheduleChanges.Paused: return WorkflowTriggerEventType.RecurringCallSchedulePaused;
					case RecurringCallScheduleChanges.Resumed: return WorkflowTriggerEventType.RecurringCallScheduleResumed;
					case RecurringCallScheduleChanges.Deleted: return WorkflowTriggerEventType.RecurringCallScheduleDeleted;
					default: return WorkflowTriggerEventType.RecurringCallScheduleUpdated;
				}
			}
		}
	}

	/// <summary>What happened to one occurrence (<see cref="RecurringCallOccurrenceEvent"/>).</summary>
	public enum RecurringCallOccurrenceChanges
	{
		/// <summary>Its call was created (ahead of time, or by a dispatcher).</summary>
		Created = 1,

		/// <summary>A dispatcher skipped it.</summary>
		Skipped = 2,

		/// <summary>Its call went out.</summary>
		Dispatched = 3
	}

	/// <summary>
	/// An occurrence of a recurring call had its call created, was skipped, or went out. Workflow triggers
	/// <see cref="WorkflowTriggerEventType.RecurringCallOccurrenceCreated"/>, <see cref="WorkflowTriggerEventType.RecurringCallOccurrenceSkipped"/>
	/// and <see cref="WorkflowTriggerEventType.RecurringCallOccurrenceDispatched"/>.
	/// </summary>
	public class RecurringCallOccurrenceEvent
	{
		public int DepartmentId { get; set; }
		public RecurringCallOccurrenceChanges Change { get; set; }
		public int RecurringCallId { get; set; }
		public string Name { get; set; }
		/// <summary>When it is (was) due, UTC.</summary>
		public DateTime OccurrenceOn { get; set; }
		public int? CallId { get; set; }
		public string CallNumber { get; set; }
		/// <summary>Who acted; null for the worker.</summary>
		public string UserId { get; set; }
		/// <summary>Created or sent by a dispatcher rather than the schedule.</summary>
		public bool ByDispatcher { get; set; }
		public int UnitCount { get; set; }
		public int PersonnelCount { get; set; }
		public int GroupCount { get; set; }
		public int RoleCount { get; set; }
		/// <summary>Chosen units and members left off because they were not available.</summary>
		public int UnavailableCount { get; set; }
		/// <summary>Whether a run card added resources.</summary>
		public bool RunCardApplied { get; set; }

		public WorkflowTriggerEventType Trigger
		{
			get
			{
				switch (Change)
				{
					case RecurringCallOccurrenceChanges.Skipped: return WorkflowTriggerEventType.RecurringCallOccurrenceSkipped;
					case RecurringCallOccurrenceChanges.Dispatched: return WorkflowTriggerEventType.RecurringCallOccurrenceDispatched;
					default: return WorkflowTriggerEventType.RecurringCallOccurrenceCreated;
				}
			}
		}
	}

	/// <summary>
	/// A recurring call alert was raised or closed. Workflow triggers <see cref="WorkflowTriggerEventType.RecurringCallAlertRaised"/> and
	/// <see cref="WorkflowTriggerEventType.RecurringCallAlertResolved"/>.
	/// </summary>
	public class RecurringCallAlertEvent
	{
		public int DepartmentId { get; set; }
		/// <summary>True when raised, false when closed.</summary>
		public bool Raised { get; set; }
		public string AlertId { get; set; }
		public int RecurringCallId { get; set; }
		public string Name { get; set; }
		/// <summary><see cref="RecurringCallAlertKinds"/>.</summary>
		public int Kind { get; set; }
		public string KindName { get; set; }
		public DateTime OccurrenceOn { get; set; }
		public int? CallId { get; set; }
		/// <summary><see cref="RecurringCallAlertReasons"/>, when there is one.</summary>
		public string Reason { get; set; }
		public int UnavailableUnitCount { get; set; }
		public int UnavailablePersonnelCount { get; set; }
		/// <summary><see cref="RecurringCallAlertResolutions"/> name, when closed.</summary>
		public string Resolution { get; set; }
		/// <summary>Who closed it; null when raised or closed by the system.</summary>
		public string UserId { get; set; }

		public WorkflowTriggerEventType Trigger => Raised ? WorkflowTriggerEventType.RecurringCallAlertRaised : WorkflowTriggerEventType.RecurringCallAlertResolved;
	}

	/// <summary>
	/// A department's recurring call alerts changed (raised, acknowledged, closed). Fans out over the eventing topic as
	/// <c>recurringCallAlertsUpdated</c> with the schedule id and a small payload, so dispatch clients reload their alert list
	/// (through the REST endpoint, which applies the viewer's dispatch scope) and can sound a new alert.
	/// </summary>
	public class RecurringCallAlertsUpdatedEvent
	{
		public int DepartmentId { get; set; }
		public int RecurringCallId { get; set; }
		/// <summary>Camel-cased JSON: { alertId, kind, raised }.</summary>
		public string Payload { get; set; }
	}
}
