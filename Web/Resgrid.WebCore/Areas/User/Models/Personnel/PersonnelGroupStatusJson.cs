using System;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Personnel
{
	public class GroupStatusJson
	{
		public int GroupId { get; set; }
		public string Name { get; set; }
		public bool CanSetGroupStatus { get; set; }
		public List<PersonnelStatusJson> Personnel { get; set; } 
	}

	public class PersonnelStatusJson
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public string Status { get; set; }
		public string StatusCss { get; set; }
		public string Staffing { get; set; }
		public string StaffingCss { get; set; }
		public DateTime LastActionDate { get; set; }
	}
}