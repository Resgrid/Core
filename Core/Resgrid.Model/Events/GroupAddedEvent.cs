namespace Resgrid.Model.Events
{
	public class GroupAddedEvent
	{
		public int DepartmentId { get; set; }
		public DepartmentGroup Group { get; set; }
	}
}

