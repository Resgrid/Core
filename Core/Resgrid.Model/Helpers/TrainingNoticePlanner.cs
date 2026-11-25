using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Helpers
{
	/// <summary>What the training notifier sends for one training on one run.</summary>
	public enum TrainingNoticeKinds
	{
		/// <summary>Nothing is sent.</summary>
		None = 0,

		/// <summary>The first notice: the training was assigned and has not been announced yet.</summary>
		NewTraining = 1,

		/// <summary>The reminder the day before the training is due.</summary>
		DueTomorrow = 2
	}

	/// <summary>The notice the training notifier sends for one training: its kind, its text and who receives it.</summary>
	public class TrainingNoticePlan
	{
		public TrainingNoticeKinds Kind { get; set; }

		public string Title { get; set; }

		public string Message { get; set; }

		public List<string> RecipientUserIds { get; set; } = new List<string>();
	}

	/// <summary>
	/// Decides what the training notifier sends for a training. Kept apart from the worker so the rules can be tested:
	/// who is told, and that an old training is never announced as new.
	/// </summary>
	public static class TrainingNoticePlanner
	{
		/// <summary>
		/// A training that was never announced is announced only while it is this recent. The notifier sent nothing for
		/// years (it loaded trainings without their assignees), so every older training still looks unannounced; without
		/// this limit the first working run would tell every assignee about every training the department ever made.
		/// </summary>
		public const int NewNoticeMaxAgeDays = 7;

		public const string NewTrainingTitle = "New Training Notice";
		public const string DueTomorrowTitle = "Training Due Notice";

		/// <summary>True when the training was never announced and is too old to announce now.</summary>
		public static bool IsStaleUnannounced(Training training, DateTime utcNow)
		{
			return training != null && !training.Notified.HasValue && training.CreatedOn < utcNow.AddDays(-NewNoticeMaxAgeDays);
		}

		/// <summary>
		/// The notice for a training. Recipients are the assignees who have not completed it and who are still active
		/// members of the department (deleted, disabled and hidden members are left out of automation).
		/// </summary>
		/// <param name="training">The training, with its assignees loaded.</param>
		/// <param name="activeMemberUserIds">The department's active member ids.</param>
		/// <param name="utcNow">The current time, UTC.</param>
		public static TrainingNoticePlan Plan(Training training, IEnumerable<string> activeMemberUserIds, DateTime utcNow)
		{
			var plan = new TrainingNoticePlan { Kind = TrainingNoticeKinds.None };

			if (training == null || IsStaleUnannounced(training, utcNow))
				return plan;

			var active = new HashSet<string>(activeMemberUserIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

			plan.RecipientUserIds = (training.Users ?? Enumerable.Empty<TrainingUser>())
				.Where(x => x != null && !x.Complete && !string.IsNullOrWhiteSpace(x.UserId) && active.Contains(x.UserId))
				.Select(x => x.UserId)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (plan.RecipientUserIds.Count == 0)
				return plan;

			if (!training.Notified.HasValue)
			{
				plan.Kind = TrainingNoticeKinds.NewTraining;
				plan.Title = NewTrainingTitle;
				plan.Message = training.ToBeCompletedBy.HasValue
					? string.Format("New Training ({0}) due on {1}", training.Name, training.ToBeCompletedBy.Value.ToShortDateString())
					: string.Format("New Training ({0}) assigned to you", training.Name);
			}
			else
			{
				plan.Kind = TrainingNoticeKinds.DueTomorrow;
				plan.Title = DueTomorrowTitle;
				plan.Message = string.Format("Training ({0}) is due tomorrow", training.Name);
			}

			return plan;
		}
	}
}
