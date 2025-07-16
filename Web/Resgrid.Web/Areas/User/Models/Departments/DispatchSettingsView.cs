using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Departments
{
	public class DispatchSettingsView
	{
		public SelectList StatusLevels { get; set; }
		public int ShiftDispatchStatus { get; set; }
		public int ShiftClearStatus { get; set; }
		public ActionTypes UserStatusTypes { get; set; }
		public bool DispatchShiftInsteadOfGroup { get; set; }
		public bool AutoSetStatusForShiftPersonnel { get; set; }

		public bool UnitDispatchAlsoDispatchToAssignedPersonnel { get; set; }
		public bool UnitDispatchAlsoDispatchToGroup { get; set; }

		public bool? SaveSuccess { get; set; }
		public string Message { get; set; }

		public DispatchSettingsView()
		{
			ShiftDispatchStatus = -1;
			ShiftClearStatus = -1;
		}
	}
}
