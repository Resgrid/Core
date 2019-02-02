namespace Resgrid.Model.Events
{
	public class CallAddedEvent
	{
		public int DepartmentId { get; set; }
		public Call Call { get; set; }
	}
}