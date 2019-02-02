namespace Resgrid.Model.Events
{
	public class ShiftTradeRequestedEvent
	{
		public int DepartmentId { get; set; }
		public int ShiftSignupTradeId { get; set; }
		public string DepartmentNumber { get; set; }
	}
}