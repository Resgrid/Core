using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models
{
	public class WelcomeModel: BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
	}
}
