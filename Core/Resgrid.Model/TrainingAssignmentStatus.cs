using System;

namespace Resgrid.Model
{
	/// <summary>A training assigned to one member and where that member stands on it (ITrainingService.GetOutstandingTrainingsForUserAsync).</summary>
	public sealed class TrainingAssignmentStatus
	{
		public Training Training { get; set; }

		/// <summary>The member's assignment row.</summary>
		public TrainingUser Assignment { get; set; }

		/// <summary>The latest quiz attempt failed (stored on the assignment, never recomputed from the current quiz).</summary>
		public bool QuizFailed { get; set; }

		/// <summary>The due moment, UTC, with leave applied (TrainingDueDates.EffectiveDueUtc). Null when nothing is due.</summary>
		public DateTime? DueUtc { get; set; }

		public TrainingAssignmentStates State { get; set; }

		public bool IsOverdue => State == TrainingAssignmentStates.Overdue;
	}
}
