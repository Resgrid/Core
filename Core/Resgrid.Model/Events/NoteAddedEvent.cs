namespace Resgrid.Model.Events
{
	public class NoteAddedEvent
	{
		public int DepartmentId { get; set; }
		public Note Note { get; set; }
	}
}