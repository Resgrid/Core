using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Departments
{
	/// <summary>
	/// Basic high level information for a department in the system
	/// </summary>
	public class DepartmentResult
	{
		/// <summary>
		/// Id of the Department
		/// </summary>
		public int Id { get; set; }

		/// <summary>
		/// The Departments name
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// The UserId of the main Admin or Managing Member
		/// </summary>
		public string ManagingUserId { get; set; }

		/// <summary>
		/// (Optional) Id of the departments main location or map center address
		/// </summary>
		public int? AddressId { get; set; }

		/// <summary>
		/// The type of department
		/// </summary>
		public string DepartmentType { get; set; }

		/// <summary>
		/// Time zone of the department
		/// </summary>
		public string TimeZone { get; set; }

		/// <summary>
		/// When was the department created
		/// </summary>
		public DateTime? CreatedOn { get; set; }

		/// <summary>
		/// When were the departments high level settings last updated
		/// </summary>
		public DateTime? UpdatedOn { get; set; }

		/// <summary>
		/// Is the department using 24 hour time?
		/// </summary>
		public bool? Use24HourTime { get; set; }

		/// <summary>
		/// The Code used to import Department Wide (All-Call) dispatches
		/// </summary>
		public string EmailCode { get; set; }

		/// <summary>
		/// List of UserId's that are Department Admins
		/// </summary>
		public List<string> AdminUsers { get; set; }

		/// <summary>
		/// List of UserId's for all members in the Deaprtment
		/// </summary>
		public List<string> Members { get; set; }
	}
}
