using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Departments
{
	public class ShiftSettingsView
	{
		public bool AllowSignupsForMultipleShiftGroups { get; set; }

		public bool? SaveSuccess { get; set; }
		public string Message { get; set; }

	}
}
