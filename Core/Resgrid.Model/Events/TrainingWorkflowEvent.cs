using System;

namespace Resgrid.Model.Events
{
	/// <summary>What happened to a member's training (<see cref="TrainingWorkflowEvent"/>).</summary>
	public enum TrainingWorkflowChanges
	{
		Assigned = 1,
		Completed = 2,
		Failed = 3,
		Overdue = 4,
		AtRisk = 5,
		Enforced = 6
	}

	/// <summary>
	/// A member's LMS training was assigned, completed, failed, became overdue or at risk, or was enforced (T3 item 5; workflow triggers
	/// 224-229), registered like the certification events so a department can automate them ("overdue 7 days → notify the captain and
	/// create a task"). The payload names the training and the requirement and carries ids and dates only: never the member's name, e-mail
	/// address or phone number (the workflow resolves the member from <see cref="UserId"/> under its own rules).
	/// </summary>
	public class TrainingWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public TrainingWorkflowChanges Change { get; set; }

		/// <summary>The member the event is about.</summary>
		public string UserId { get; set; }

		/// <summary>"Requirement", "Course", "Path" or "Assessment".</summary>
		public string Subject { get; set; }

		/// <summary>The learning requirement, when the event comes from one.</summary>
		public int? RequirementId { get; set; }

		public string RequirementName { get; set; }

		/// <summary><c>LmsRequirementKinds</c> as its name ("Course", "CategoryHours"...), or null.</summary>
		public string RequirementKind { get; set; }

		public int? CourseId { get; set; }

		public int? PathId { get; set; }

		/// <summary>The course, training, category or path the member is asked to finish.</summary>
		public string TrainingName { get; set; }

		/// <summary><c>LmsRequirementStates</c> as its name after the change ("Overdue").</summary>
		public string State { get; set; }

		public DateTime? DueOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		/// <summary>Days past the due date (overdue), or left before it (at risk); null otherwise.</summary>
		public int? Days { get; set; }

		/// <summary>Enforcement: the certification type the requirement backs.</summary>
		public int? CertificationTypeId { get; set; }

		public string CertificationTypeName { get; set; }

		/// <summary>An assessment's score (failed), as a percentage.</summary>
		public decimal? Score { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger
		{
			get
			{
				switch (Change)
				{
					case TrainingWorkflowChanges.Completed: return WorkflowTriggerEventType.TrainingCompleted;
					case TrainingWorkflowChanges.Failed: return WorkflowTriggerEventType.TrainingFailed;
					case TrainingWorkflowChanges.Overdue: return WorkflowTriggerEventType.TrainingOverdue;
					case TrainingWorkflowChanges.AtRisk: return WorkflowTriggerEventType.RequirementAtRisk;
					case TrainingWorkflowChanges.Enforced: return WorkflowTriggerEventType.RequirementEnforced;
					default: return WorkflowTriggerEventType.TrainingAssigned;
				}
			}
		}
	}
}
