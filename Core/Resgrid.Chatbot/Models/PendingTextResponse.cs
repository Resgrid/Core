namespace Resgrid.Chatbot.Models
{
	public enum PendingTextResponseType
	{
		Poll = 1,
		CalendarRsvp = 2
	}

	public class PendingTextResponse
	{
		public PendingTextResponseType Type { get; set; }
		public int SourceId { get; set; }
		public int MessageId { get; set; }
		public string Label { get; set; }
	}
}
