using Resgrid.Model;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Web.Areas.User.Models.Profile
{
	public class ReportingView
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
	}
}