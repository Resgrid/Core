namespace Resgrid.Model.Events
{
	public class ShiftCreatedEvent
	{
		public int DepartmentId { get; set; }
		public string DepartmentNumber { get; set; }
		public Shift Item { get; set; }
	}
}