namespace Resgrid.Model.Events
{
	public class UnitStatusEvent
	{
		public int DepartmentId { get; set; }
		public UnitState Status { get; set; }
		public UnitState PreviousStatus { get; set; }
	}
}