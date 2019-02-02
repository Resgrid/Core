namespace Resgrid.Model.Events
{
	public class CalendarEventUpdatedEvent
	{
		public int DepartmentId { get; set; }
		public CalendarItem Item { get; set; }
	}
}