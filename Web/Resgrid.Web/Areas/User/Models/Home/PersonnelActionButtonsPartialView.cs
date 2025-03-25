using System;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Home
{
	public class PersonnelActionButtonsPartialView
	{
		public string UserId { get; set; }
		public CustomState States { get; set; }
		public CustomState StaffingLevels { get; set; }
	}
}