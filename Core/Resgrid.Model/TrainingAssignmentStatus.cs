namespace Resgrid.Model
{
	/// <summary>A training assigned to one member and where that member stands on it (ITrainingService.GetOutstandingTrainingsForUserAsync).</summary>
	public sealed class TrainingAssignmentStatus
	{
		public Training Training { get; set; }

		/// <summary>The member's assignment row.</summary>
		public TrainingUser Assignment { get; set; }

		/// <summary>Completed, but the quiz score is below the training's minimum (the page's red "complete" state).</summary>
		public bool QuizFailed { get; set; }
	}
}
