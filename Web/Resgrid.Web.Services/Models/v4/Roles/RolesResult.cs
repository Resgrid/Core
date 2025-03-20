using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Roles
{
	/// <summary>
	/// A role in the Resgrid system
	/// </summary>
	public class RolesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<RoleResultData> Data { get; set; }
	}
}
