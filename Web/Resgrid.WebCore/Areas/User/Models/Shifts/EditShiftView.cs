using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	public class EditShiftView
	{
		public string Message { get; set; }
		public Shift Shift { get; set; }
		public ShiftAssignmentTypes AssignmentType { get; set; }
		public string Dates { get; set; }
		public List<DepartmentGroup> Groups { get; set; }
	}
}