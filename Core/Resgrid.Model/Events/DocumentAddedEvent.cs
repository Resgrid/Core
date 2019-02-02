namespace Resgrid.Model.Events
{
	public class DocumentAddedEvent
	{
		public int DepartmentId { get; set; }
		public Document Document { get; set; }
	}
}