using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Security
{
	public class DepartmentRightsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public DepartmentRightsResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public DepartmentRightsResult()
		{
			Data = new DepartmentRightsResultData();
		}
	}

	/// <summary>
	/// Object that denotes the right assignments for a user in a department
	/// </summary>
	public class DepartmentRightsResultData
	{
		/// <summary>
		/// Department name
		/// </summary>
		public string DepartmentName { get; set; }

		/// <summary>
		/// Department code
		/// </summary>
		public string DepartmentCode { get; set; }

		/// <summary>
		/// Users full name
		/// </summary>
		public string FullName { get; set; }

		/// <summary>
		/// Email address
		/// </summary>
		public string EmailAddress { get; set; }

		/// <summary>
		/// Department id
		/// </summary>
		public string DepartmentId { get; set; }

		/// <summary>
		/// Is the user a department admin
		/// </summary>
		public bool IsAdmin { get; set; }

		/// <summary>
		/// Can the user view PII
		/// </summary>
		public bool CanViewPII { get; set; }

		/// <summary>
		/// Can the user create calls
		/// </summary>
		public bool CanCreateCalls { get; set; }

		/// <summary>
		/// Can the user add a note
		/// </summary>
		public bool CanAddNote { get; set; }

		/// <summary>
		/// Can the user create messages
		/// </summary>
		public bool CanCreateMessage { get; set; }

		/// <summary>
		/// Groups in the department the user is a member of
		/// </summary>
		public List<GroupRightData> Groups { get; set; }
	}

	/// <summary>
	/// Object containting a group right assignemnt
	/// </summary>
	public class GroupRightData
	{
		/// <summary>
		/// Id of the group this right assignement is for
		/// </summary>
		public string GroupId { get; set; }

		/// <summary>
		/// Is the user a group admin
		/// </summary>
		public bool IsGroupAdmin { get; set; }
	}
}
