using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Profile
{
	public class EditStaffingLevelView : BaseUserModel
	{
		public int ScheduleId { get; set; }
		public string Message { get; set; }
		public bool SpecificDatetime { get; set; }
		public string SpecifcDate { get; set; }
		public string Time { get; set; }
		public SelectList StaffingLevels { get; set; }
		public int StaffingLevel { get; set; }
		public UserStateTypes StaffingLevelEnum { get; set; }
		public bool Sunday { get; set; }
		public bool Monday { get; set; }
		public bool Tuesday { get; set; }
		public bool Wednesday { get; set; }
		public bool Thursday { get; set; }
		public bool Friday { get; set; }
		public bool Saturday { get; set; }
	}
}