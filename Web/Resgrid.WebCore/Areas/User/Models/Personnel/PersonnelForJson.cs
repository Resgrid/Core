using System;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Personnel
{
	public class PersonnelForJson
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public string FirstName { get; set; }
		public string LastName { get; set; }
		public string Group { get; set; }
		public int GroupId { get; set; }
		public string Status { get; set; }
		public string StatusColor { get; set; }
		public string Staffing { get; set; }
		public string StaffingColor { get; set; }
		public List<string> Roles { get; set; }
		public string Eta { get; set; }
		public int Weight { get; set; }
	}

	public class PersonnelForListJson
	{
		public string Name { get; set; }
		public string EmailAddress { get; set; }
		public string Group { get; set; }
		public string Roles { get; set; }
		public string LastActivityDate { get; set; }
		public string State { get; set; }
		public int StatusId { get; set; }
		public int StaffingId { get; set; }
		public string UserId { get; set; }
		public bool CanRemoveUser { get; set; }
		public bool CanEditUser { get; set; }
		public string FirstName { get; set; }
		public string LastName { get; set; }
		public int GroupId { get; set; }
	}
}
