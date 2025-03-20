namespace Resgrid.Web.Areas.User.Models.Calendar
{
	public class CalendarItemJson
	{
		public int CalendarItemId { get; set; }

		public string Title { get; set; }

		public string Start { get; set; }

		public string End { get; set; }

		public string StartTimezone { get; set; }

		public string EndTimezone { get; set; }

		public string Description { get; set; }

		public string RecurrenceId { get; set; }

		public string RecurrenceRule { get; set; }

		public string RecurrenceException { get; set; }

		public int? ItemType { get; set; }

		public bool Public { get; set; }

		public bool IsAllDay { get; set; }

		public string Location { get; set; }

		public int SignupType { get; set; }

		public int Reminder { get; set; }

		public bool LockEditing { get; set; }

		public string Entities { get; set; }

		public string RequiredAttendes { get; set; }

		public string OptionalAttendes { get; set; }

		public bool IsAdminOrCreator { get; set; }

		public string CreatorUserId { get; set; }
	}
}