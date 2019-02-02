using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Personnel
{
	public class PersonnelRolesModel : BaseUserModel
	{
		public List<PersonnelRole> Roles { get; set; }
		public bool CanAddNewRole { get; set; }

		public PersonnelRolesModel()
		{
			CanAddNewRole = true;
		}
	}
}