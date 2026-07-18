using System;

namespace Resgrid.Model.Messages
{
	/// <summary>
	/// Machine-readable metadata stored on a message recipient for prompts that can be answered from
	/// any text channel. Keeping it on the recipient allows response state to remain user-specific.
	/// </summary>
	public static class TextResponsePromptMetadata
	{
		private const string CalendarRsvpPrefix = "calendar-rsvp:";
		private const string PollPrefix = "poll:";

		public static string ForCalendarRsvp(int calendarItemId)
			=> CalendarRsvpPrefix + calendarItemId;

		public static bool TryGetCalendarItemId(string note, out int calendarItemId)
		{
			calendarItemId = 0;
			if (string.IsNullOrWhiteSpace(note)
				|| !note.StartsWith(CalendarRsvpPrefix, StringComparison.OrdinalIgnoreCase))
				return false;

			return int.TryParse(note.Substring(CalendarRsvpPrefix.Length), out calendarItemId)
				&& calendarItemId > 0;
		}

		public static string ForPoll(int departmentId)
			=> PollPrefix + departmentId;

		public static bool TryGetPollDepartmentId(string note, out int departmentId)
		{
			departmentId = 0;
			if (string.IsNullOrWhiteSpace(note)
				|| !note.StartsWith(PollPrefix, StringComparison.OrdinalIgnoreCase))
				return false;

			return int.TryParse(note.Substring(PollPrefix.Length), out departmentId)
				&& departmentId > 0;
		}
	}
}
