using System;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Settings
{
	public class SettingsAddressModel : BaseUserModel
	{
		public string Message { get; set; }
		public string ErrorMessage { get; set; }

		public string UserId { get; set; }
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
	}
}
