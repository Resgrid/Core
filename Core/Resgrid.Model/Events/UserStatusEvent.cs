namespace Resgrid.Model.Events
{
	public class UserStatusEvent
	{
		public int DepartmentId { get; set; }
		public ActionLog PreviousStatus { get; set; }
		public ActionLog Status { get; set; }
	}
}