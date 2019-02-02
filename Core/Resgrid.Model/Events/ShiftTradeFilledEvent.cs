using System;

namespace Resgrid.Model.Events
{
	public class ShiftTradeFilledEvent
	{
		public int ShiftSignupTradeId { get; set; }
		public string UserId { get; set; }
		public int DepartmentId { get; set; }
		public string DepartmentNumber { get; set; }
	}
}