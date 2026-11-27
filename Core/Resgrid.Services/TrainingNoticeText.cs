using System;
using System.Globalization;
using Resgrid.Localization.Areas.User.ContentNotices;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Services
{
	/// <summary>
	/// The training notifier's text in the reader's language (round 9C). <see cref="TrainingNoticePlanner"/> decides what each assignee is
	/// sent (and keeps its English wording for the rules' tests); this writes that notice from ContentNotices in the member's culture, with
	/// the due date in the department's zone written the member's way. The training's own reminder message is the department's text and is
	/// appended as it was written.
	/// </summary>
	public static class TrainingNoticeText
	{
		public static ContentNoticeDelivery.Notice Render(TrainingNoticePlan plan, Training training, DateTime? effectiveDueUtc, Department department, CultureInfo culture)
		{
			if (plan == null || training == null || plan.Kind == TrainingNoticeKinds.None)
				return new ContentNoticeDelivery.Notice(plan?.Title, plan?.Message);

			var due = ContentNoticeCultures.LocalDate(effectiveDueUtc, department, culture);
			var custom = string.IsNullOrWhiteSpace(training.ReminderMessage) ? string.Empty : " " + training.ReminderMessage.Trim();
			switch (plan.Kind)
			{
				case TrainingNoticeKinds.NewTraining:
					return new ContentNoticeDelivery.Notice(ContentNoticeText.Get("Training.New.Title", culture), (due == null
						? ContentNoticeText.Format("Training.New.Message", culture, training.Name)
						: ContentNoticeText.Format("Training.New.MessageDue", culture, training.Name, due)) + custom);
				case TrainingNoticeKinds.Overdue:
					return new ContentNoticeDelivery.Notice(ContentNoticeText.Get("Training.Overdue.Title", culture), (due == null
						? ContentNoticeText.Format("Training.Overdue.Message", culture, training.Name)
						: ContentNoticeText.Format("Training.Overdue.MessageDue", culture, training.Name, due)) + custom);
				default:
					var lead = plan.LeadDays ?? 1;
					var message = lead == 1
						? due == null ? ContentNoticeText.Format("Training.Due.OneDay", culture, training.Name) : ContentNoticeText.Format("Training.Due.OneDayOn", culture, training.Name, due)
						: due == null ? ContentNoticeText.Format("Training.Due.Days", culture, training.Name, lead) : ContentNoticeText.Format("Training.Due.DaysOn", culture, training.Name, lead, due);
					return new ContentNoticeDelivery.Notice(ContentNoticeText.Get("Training.Due.Title", culture), message + custom);
			}
		}
	}
}
