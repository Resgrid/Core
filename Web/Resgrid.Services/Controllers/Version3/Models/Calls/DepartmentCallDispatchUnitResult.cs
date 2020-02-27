using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Calls
{
	public class DepartmentCallDispatchUnitResult
	{
		public int CallDispatchUnitId { get; set; }
		public int CallId { get; set; }
		public int UnitId { get; set; }
		public int DispatchCount { get; set; }
		public DateTime? LastDispatchedOn { get; set; }
	}
}
