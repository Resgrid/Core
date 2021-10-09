namespace Resgrid.Model.Events
{
	public class CallUpdatedEvent
	{
		public int DepartmentId { get; set; }
		public Call Call { get; set; }
	}
}
