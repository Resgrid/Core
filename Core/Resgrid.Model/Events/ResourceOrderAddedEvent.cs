namespace Resgrid.Model.Events
{
	public class ResourceOrderAddedEvent
	{
		public int DepartmentId { get; set; }
		public ResourceOrder Order { get; set; }
	}
}
