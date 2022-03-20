using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v3.Dispatch
{
	public class PersonnelForCallResult
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
		public string Location { get; set; }
	}
}
