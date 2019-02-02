using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Reports.Personnel
{
	public class PersonnelReportRow
	{
		public string Name { get; set; }
		public string Username { get; set; }
		public string ID { get; set; }
		public string DepartmentRole { get; set; }
		public string Group { get; set; }
		public string Roles { get; set; }
		public string MobilePhoneNumber { get; set; }
		public string MailingAddress { get; set; }
		public string Email { get; set; }
	}

	public class PersonnelReportView
	{
		public Department Department { get; set; }
		public DateTime RunOn { get; set; }
		public List<PersonnelReportRow> Rows { get; set; }
	}
}