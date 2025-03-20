using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.Logs
{
	public class NewCallLogView : BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public string Message { get; set; }
		public CallLog Log { get; set; }
		public SelectList CallsList { get; set; } 
	}
}
