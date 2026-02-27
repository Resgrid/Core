namespace Resgrid.Model.Events
{
	public class GroupUpdatedEvent
	{
		public int DepartmentId { get; set; }
		public DepartmentGroup Group { get; set; }
	}
}

