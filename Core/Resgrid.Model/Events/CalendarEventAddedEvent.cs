namespace Resgrid.Model.Events
{
	public class CalendarEventAddedEvent
	{
		public int DepartmentId { get; set; }
		public CalendarItem Item { get; set; }
	}
}