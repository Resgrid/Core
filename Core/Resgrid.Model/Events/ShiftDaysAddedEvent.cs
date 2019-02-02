using System.Collections.Generic;

namespace Resgrid.Model.Events
{
	public class ShiftDaysAddedEvent
	{
		public int DepartmentId { get; set; }
		public string DepartmentNumber { get; set; }
		public Shift Item { get; set; }
		public List<ShiftDay> Days { get; set; }
	}
}