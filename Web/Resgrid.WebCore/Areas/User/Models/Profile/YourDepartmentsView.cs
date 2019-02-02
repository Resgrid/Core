using Resgrid.Model;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Profile
{
	public class YourDepartmentsView
	{
		public string UserId { get; set; }
		public List<DepartmentMember> Members { get; set; }
	}
}