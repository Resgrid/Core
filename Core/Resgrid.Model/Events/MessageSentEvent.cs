namespace Resgrid.Model.Events
{
	public class MessageSentEvent
	{
		public int DepartmentId { get; set; }
		public Message Message { get; set; }
	}
}

