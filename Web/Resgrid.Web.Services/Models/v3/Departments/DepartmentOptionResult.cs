using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Departments
{
	/// <summary>
	/// Represents department options set in other parts of the system, for example email and text messaging call format options
	/// </summary>
	public class DepartmentOptionResult
	{
		/// <summary>
		/// Department Id for the options
		/// </summary>
		public int DepartmentId { get; set; }

		/// <summary>
		/// Format type used for import call emails
		/// </summary>
		public int EmailFormatType { get; set; }
	}
}
