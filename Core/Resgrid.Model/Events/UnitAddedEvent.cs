namespace Resgrid.Model.Events
{
	public class UnitAddedEvent
	{
		public int DepartmentId { get; set; }
		public Unit Unit { get; set; }
	}
}