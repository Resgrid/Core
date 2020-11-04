using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Security
{
	/// <summary>
	/// Object that denotes the right assignments for a user in a department
	/// </summary>
	public class DepartmentRightsResult
	{
		/// <summary>
		/// Is the user a department admin
		/// </summary>
		public bool Adm { get; set; }

		/// <summary>
		/// Can the user view PII
		/// </summary>
		public bool VPii { get; set; }

		/// <summary>
		/// Can the user create calls
		/// </summary>
		public bool CCls { get; set; }

		/// <summary>
		/// Can the user add a note
		/// </summary>
		public bool ANot { get; set; }

		/// <summary>
		/// Can the user create messages
		/// </summary>
		public bool CMsg { get; set; }

		/// <summary>
		/// Groups in the department the user is a member of
		/// </summary>
		public List<GroupRight> Grps { get; set; }

		public string FirebaseApiToken { get; set; }
	}

	/// <summary>
	/// Object containting a group right assignemnt
	/// </summary>
	public class GroupRight
	{
		/// <summary>
		/// Id of the group this right assignement is for
		/// </summary>
		public int Gid { get; set; }

		/// <summary>
		/// Is the user a group admin
		/// </summary>
		public bool Adm { get; set; }
	}
}
