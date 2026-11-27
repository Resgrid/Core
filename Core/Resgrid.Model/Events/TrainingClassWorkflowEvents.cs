using System;

namespace Resgrid.Model.Events
{
	// Round 7B workflow triggers 240-247 (registered like 224-229): class sessions, the waitlist, completions recorded by an instructor and
	// external learners. Payloads carry ids, titles, dates and counts only: never a member's or learner's name, e-mail address or phone number
	// (a workflow resolves a member from the user id under its own rules; an external learner's "xl-" id resolves to nothing member-shaped).
	// Plain baseline payload types (no LMS types), so the workflow catalog may reference them.

	/// <summary>What happened to a class session (<see cref="TrainingClassWorkflowEvent"/>).</summary>
	public enum TrainingClassWorkflowChanges
	{
		Scheduled = 1,
		Cancelled = 2,
		SignedOff = 3,
		WaitlistPromoted = 4
	}

	/// <summary>A class session was scheduled, cancelled or signed off, or someone was promoted from its waitlist (triggers 240-243).</summary>
	public class TrainingClassWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public TrainingClassWorkflowChanges Change { get; set; }

		public int SessionId { get; set; }

		public string Title { get; set; }

		/// <summary>"None", "Course" or "Training": what the class teaches.</summary>
		public string CourseKind { get; set; }

		public int? CourseId { get; set; }

		public int? TrainingId { get; set; }

		public DateTime StartsOn { get; set; }

		public DateTime EndsOn { get; set; }

		/// <summary>Seats (0: no limit).</summary>
		public int Capacity { get; set; }

		public int Enrolled { get; set; }

		public int Waitlisted { get; set; }

		/// <summary>Sign-off: attendees and absentees.</summary>
		public int Attended { get; set; }

		public int Absent { get; set; }

		/// <summary>A series' sessions created together share it.</summary>
		public string SeriesKey { get; set; }

		/// <summary>Waitlist promotion: the member (or the external learner's "xl-" id) who got the seat.</summary>
		public string UserId { get; set; }

		/// <summary>Waitlist promotion: the promoted person is an external learner, not a member.</summary>
		public bool IsExternalLearner { get; set; }

		/// <summary>The member who scheduled, cancelled or signed it off (null for the worker).</summary>
		public string ActingUserId { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger
		{
			get
			{
				switch (Change)
				{
					case TrainingClassWorkflowChanges.Cancelled: return WorkflowTriggerEventType.ClassSessionCancelled;
					case TrainingClassWorkflowChanges.SignedOff: return WorkflowTriggerEventType.ClassSessionSignedOff;
					case TrainingClassWorkflowChanges.WaitlistPromoted: return WorkflowTriggerEventType.ClassWaitlistPromoted;
					default: return WorkflowTriggerEventType.ClassSessionScheduled;
				}
			}
		}
	}

	/// <summary>
	/// An instructor recorded that a member completed a training or course they took together (trigger 244, permission 135): one event per
	/// member recorded. Metadata only: the target, the source (a class or manual), who recorded it and the records it produced.
	/// </summary>
	public class TrainingCompletionRecordedEvent
	{
		public int DepartmentId { get; set; }

		public int RecordingId { get; set; }

		/// <summary>The member (or an external learner's "xl-" id from a class roster).</summary>
		public string UserId { get; set; }

		public bool IsExternalLearner { get; set; }

		public string RecordedByUserId { get; set; }

		/// <summary>"Training" or "Course".</summary>
		public string TargetKind { get; set; }

		public int TargetId { get; set; }

		public string TargetName { get; set; }

		/// <summary>"Manual" or "ClassSession".</summary>
		public string Source { get; set; }

		/// <summary>The class session the recording came from (0 when manual).</summary>
		public int? SessionId { get; set; }

		public int? LearningRecordId { get; set; }

		public int? CertificationId { get; set; }

		public bool QuizWaived { get; set; }

		public string AttestationVersion { get; set; }

		public DateTime CompletedOn { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger => WorkflowTriggerEventType.TrainingCompletionRecorded;
	}

	/// <summary>What happened to an external learner (<see cref="ExternalLearnerWorkflowEvent"/>).</summary>
	public enum ExternalLearnerWorkflowChanges
	{
		/// <summary>The account became active: a verified self-registration or an accepted invitation.</summary>
		Registered = 1,
		/// <summary>A class place (enrolled or waitlisted) or a course enrolment.</summary>
		Enrolled = 2,
		CompletedCourse = 3
	}

	/// <summary>
	/// An external learner registered, enrolled or completed a course (triggers 245-247). The learner is named by ids only: the learner id
	/// and the "xl-" user id, never the name, e-mail address or organization.
	/// </summary>
	public class ExternalLearnerWorkflowEvent
	{
		public int DepartmentId { get; set; }

		public ExternalLearnerWorkflowChanges Change { get; set; }

		public int LearnerId { get; set; }

		/// <summary>The learner's synthetic "xl-" user id.</summary>
		public string LearnerUserId { get; set; }

		/// <summary>"Invited" or "SelfRegistered".</summary>
		public string Source { get; set; }

		/// <summary>Enrolled: "Class" or "Course"; CompletedCourse: "Course".</summary>
		public string Subject { get; set; }

		public int? SessionId { get; set; }

		public int? CourseId { get; set; }

		/// <summary>The class's or course's title.</summary>
		public string Title { get; set; }

		/// <summary>A class place: "Enrolled", "Waitlisted" or "PendingApproval"; a course: "Enrolled" or "Completed".</summary>
		public string Status { get; set; }

		/// <summary>A class place: the fee due (0 when none or waived).</summary>
		public decimal? Fee { get; set; }

		public string Currency { get; set; }

		public DateTime? StartsOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		public int? LearningRecordId { get; set; }

		public DateTime OccurredOn { get; set; }

		public WorkflowTriggerEventType Trigger
		{
			get
			{
				switch (Change)
				{
					case ExternalLearnerWorkflowChanges.Enrolled: return WorkflowTriggerEventType.ExternalLearnerEnrolled;
					case ExternalLearnerWorkflowChanges.CompletedCourse: return WorkflowTriggerEventType.ExternalLearnerCompletedCourse;
					default: return WorkflowTriggerEventType.ExternalLearnerRegistered;
				}
			}
		}
	}
}
