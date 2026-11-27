using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Resgrid.Model.Helpers
{
	/// <summary>What the training notifier sends one assignee on one run.</summary>
	public enum TrainingNoticeKinds
	{
		/// <summary>Nothing is sent.</summary>
		None = 0,

		/// <summary>The first notice: the member was assigned and has not been told yet.</summary>
		NewTraining = 1,

		/// <summary>A due reminder: the assignment is due within one of the training's reminder leads.</summary>
		DueReminder = 2,

		/// <summary>The assignment is overdue (once, or on the training's repeating cadence).</summary>
		Overdue = 3
	}

	/// <summary>The notice for one assignee, and the stamps to write back on the assignment row.</summary>
	public class TrainingNoticePlan
	{
		public TrainingNoticeKinds Kind { get; set; }

		public string Title { get; set; }

		public string Message { get; set; }

		public string UserId { get; set; }

		/// <summary>Stamp NotifiedOn without sending (an assignment too old to announce as new).</summary>
		public bool MarkNotifiedOnly { get; set; }

		/// <summary>The reminder lead (days) this plan reminds for.</summary>
		public int? LeadDays { get; set; }
	}

	/// <summary>
	/// Decides what the training notifier sends each assignee (T-D9, T-D19, T0 item 4). Kept apart from the worker so the
	/// rules can be tested:
	/// <list type="bullet">
	/// <item>a new assignment is announced once, never when it is older than <see cref="NewNoticeMaxAgeDays"/> (the
	/// notifier sent nothing for years, so old rows still look unannounced);</item>
	/// <item>a due reminder goes out when the due moment is within a lead ("due within the next N hours and not yet
	/// reminded"), once per lead, recorded on the assignee: no 10:00 window, so any zone and offset is reached;</item>
	/// <item>an overdue notice goes out once, or every <see cref="Training.OverdueRepeatDays"/> days;</item>
	/// <item>nothing goes to completed, exempt, removed or inactive members, members on leave, or before a scheduled
	/// start.</item>
	/// </list>
	/// The planner decides what is sent; the text is rendered in each recipient's language by TrainingNoticeText (round 9C, ContentNotices resources).
	/// </summary>
	public static class TrainingNoticePlanner
	{
		/// <summary>An assignment is announced as new only while it is this recent.</summary>
		public const int NewNoticeMaxAgeDays = 7;

		public const string NewTrainingTitle = "New Training Notice";
		public const string DueReminderTitle = "Training Due Notice";
		public const string OverdueTitle = "Training Overdue Notice";

		/// <summary>True when the training was never announced and is too old to announce now.</summary>
		public static bool IsStaleUnannounced(Training training, DateTime utcNow)
		{
			return training != null && !training.Notified.HasValue && training.CreatedOn < utcNow.AddDays(-NewNoticeMaxAgeDays);
		}

		/// <summary>
		/// The notice for one assignment on this run.
		/// </summary>
		/// <param name="training">The training.</param>
		/// <param name="assignment">The assignee's row.</param>
		/// <param name="effectiveDueUtc">The due moment with leave applied (TrainingDueDates.EffectiveDueUtc).</param>
		/// <param name="isActiveMember">The member is an active member of the department.</param>
		/// <param name="onLeave">A leave period is running: due dates and reminders pause.</param>
		/// <param name="localDueText">The due date as the department reads it (short date), for the message.</param>
		/// <param name="utcNow">The current time, UTC.</param>
		public static TrainingNoticePlan PlanAssignment(Training training, TrainingUser assignment, DateTime? effectiveDueUtc, bool isActiveMember,
			bool onLeave, string localDueText, DateTime utcNow)
		{
			var plan = new TrainingNoticePlan { Kind = TrainingNoticeKinds.None, UserId = assignment?.UserId };

			if (training == null || assignment == null || string.IsNullOrWhiteSpace(assignment.UserId) || training.IsArchived || !training.HasStarted(utcNow))
				return plan;

			if (!assignment.IsOpenOn(utcNow))
				return plan;

			var custom = string.IsNullOrWhiteSpace(training.ReminderMessage) ? string.Empty : " " + training.ReminderMessage.Trim();

			if (!assignment.NotifiedOn.HasValue)
			{
				// The assignment's age: when it was made, or (rows from before T0) when the training was made.
				var assignedOn = assignment.AssignedOn ?? training.CreatedOn;
				var startedOn = training.StartsOn.HasValue && training.StartsOn.Value > assignedOn ? training.StartsOn.Value : assignedOn;

				if (startedOn < utcNow.AddDays(-NewNoticeMaxAgeDays) || !isActiveMember)
				{
					plan.MarkNotifiedOnly = true;
					return plan;
				}

				if (onLeave)
					return plan;

				plan.Kind = TrainingNoticeKinds.NewTraining;
				plan.Title = NewTrainingTitle;
				plan.Message = (effectiveDueUtc.HasValue && !string.IsNullOrWhiteSpace(localDueText)
					? string.Format(CultureInfo.InvariantCulture, "New Training ({0}) due on {1}", training.Name, localDueText)
					: string.Format(CultureInfo.InvariantCulture, "New Training ({0}) assigned to you", training.Name)) + custom;
				return plan;
			}

			if (!isActiveMember || onLeave || !effectiveDueUtc.HasValue)
				return plan;

			if (TrainingDueDates.IsOverdue(effectiveDueUtc, utcNow))
			{
				if (!training.SendOverdueNotices)
					return plan;

				var repeat = training.OverdueRepeatDays.GetValueOrDefault();
				var due = !assignment.OverdueNotifiedOn.HasValue || (repeat > 0 && assignment.OverdueNotifiedOn.Value <= utcNow.AddDays(-repeat));
				if (!due)
					return plan;

				plan.Kind = TrainingNoticeKinds.Overdue;
				plan.Title = OverdueTitle;
				plan.Message = string.Format(CultureInfo.InvariantCulture, "Training ({0}) is overdue", training.Name)
					+ (string.IsNullOrWhiteSpace(localDueText) ? string.Empty : string.Format(CultureInfo.InvariantCulture, " (due {0})", localDueText)) + custom;
				return plan;
			}

			// The smallest lead the due moment is already inside, if it was not reminded for that lead (or a smaller one).
			var lead = TrainingDueDates.ParseLeadDays(training.ReminderLeadDaysCsv)
				.Where(days => TrainingDueDates.IsDueWithin(effectiveDueUtc, utcNow, TimeSpan.FromDays(days)))
				.DefaultIfEmpty(0)
				.Min();

			if (lead <= 0 || (assignment.LastReminderLeadDays.HasValue && assignment.LastReminderLeadDays.Value <= lead))
				return plan;

			plan.Kind = TrainingNoticeKinds.DueReminder;
			plan.Title = DueReminderTitle;
			plan.LeadDays = lead;
			plan.Message = (lead == 1
				? string.Format(CultureInfo.InvariantCulture, "Training ({0}) is due within a day", training.Name)
				: string.Format(CultureInfo.InvariantCulture, "Training ({0}) is due within {1} days", training.Name, lead))
				+ (string.IsNullOrWhiteSpace(localDueText) ? string.Empty : string.Format(CultureInfo.InvariantCulture, " ({0})", localDueText)) + custom;
			return plan;
		}

		/// <summary>The overdue digest for the training's creator: how many active assignees went overdue on this run.</summary>
		public static string CreatorOverdueMessage(Training training, int overdueCount)
		{
			return string.Format(CultureInfo.InvariantCulture, "{0} member(s) are overdue on training ({1})", overdueCount, training?.Name);
		}
	}
}
