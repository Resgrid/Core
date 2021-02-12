using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models
{
	public class CallsDashboardModel: BaseUserModel
	{
		public string Latitude { get; set; }
		public string Longitude { get; set; }
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public List<Call> Calls { get; set; }
		public Call NewCall { get; set; }
		public Call ViewCall { get; set; }
		public string ModalCssClass { get; set; }
		public string ViewModalCssClass { get; set; }
		public string ViewModalStyle { get; set; }
		public string EditModalCssClass { get; set; }
		public string EditModalStyle { get; set; }
		public string Message { get; set; }
		public string Year { get; set; }
		public List<SelectListItem> Years { get; set; }
	}
}
