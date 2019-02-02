namespace Resgrid.Model.Events
{
	public class LogAddedEvent
	{
		public int DepartmentId { get; set; }
		public Log Log { get; set; }
	}
}