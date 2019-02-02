namespace Resgrid.Model.Events
{
	public class UserStaffingEvent
	{
		public int DepartmentId { get; set; }
		public UserState Staffing { get; set; }
		public UserState PreviousStaffing { get; set; }
	}
}