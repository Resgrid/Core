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
	}
}
