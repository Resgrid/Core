using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Departments
{
	public class DepartmentStatusResult
	{
		public DateTime Lup { get; set; }
		public List<string> Users { get; set; }

		public DepartmentStatusResult()
		{
			Users = new List<string>();
		}
	}
}