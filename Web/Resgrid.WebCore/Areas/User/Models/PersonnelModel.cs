using System;
using System.Collections.Generic;
using Resgrid.Model;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Web.Areas.User.Models
{
	public class PersonnelModel: BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public List<IdentityUser> Users { get; set; }
		public Dictionary<Guid, string> LastActivityDates { get; set; }
		public Dictionary<Guid, string> States { get; set; }
		public Dictionary<Guid, DepartmentGroup> Groups { get; set; }
		public bool CanAddNewUser { get; set; }
		public bool CanGroupAdminsAdd { get; set; }
  }
}