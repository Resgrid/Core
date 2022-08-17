using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Reports.Calls
{
	public class OpenCallResourceView
	{
		public DateTime RunOn { get; set; }
		public Department Department { get; set; }
		public List<OpenCallResource> Calls { get; set; }
		public OpenCallResourceView()
		{
			Calls = new List<OpenCallResource>();
		}
	}

	public class OpenCallResource
	{
		public string Number { get; set; }
		public string Name { get; set; }
		public string Type { get; set; }
		public DateTime LoggedOn { get; set; }
		public int UnitsCount { get; set; }
		public int PersonnelCount { get; set; }
		public List<OpenCallResourceUnit> Units { get; set; }
		public List<OpenCallResourcePerson> Personnel { get; set; }

		public OpenCallResource()
		{
			Units = new List<OpenCallResourceUnit>();
			Personnel = new List<OpenCallResourcePerson>();
		}
	}

	public class OpenCallResourceUnit
	{
		public string UnitName { get; set; }
		public DateTime DispatchedOn { get; set; }
		public Dictionary<string, OpenCallResourcePerson> Roles { get; set; }

		public OpenCallResourceUnit()
		{
			Roles = new Dictionary<string, OpenCallResourcePerson>();
		}
	}
	
	public class OpenCallResourcePerson
	{
		public string Name { get; set; }
		public string Roles { get; set; }
		public string GroupName { get; set; }
		public DateTime DispatchedOn { get; set; }
	}
}
