using Resgrid.Model;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Web.Areas.User.Models
{
	public class WelcomeModel: BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
	}
}