using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.UnitRoles
{
	/// <summary>
	/// Multiple Unit Roles Result
	/// </summary>
	public class UnitRolesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<UnitRoleResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public UnitRolesResult()
		{
			Data = new List<UnitRoleResultData>();
		}
	}
}
