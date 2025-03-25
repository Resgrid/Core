using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.UnitRoles
{
	/// <summary>
	/// Gets the users assigned to the accountability roles for a unit
	/// </summary>
	public class ActiveUnitRolesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<ActiveUnitRoleResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public ActiveUnitRolesResult()
		{
			Data = new List<ActiveUnitRoleResultData>();
		}
	}

	/// <summary>
	/// A unit role
	/// </summary>
	public class ActiveUnitRoleResultData : UnitRoleResultData
	{
		/// <summary>
		/// UserId assigned to the role
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// Users full name
		/// </summary>
		public string FullName { get; set; }

		/// <summary>
		/// When the user was assigned to the role
		/// </summary>
		public string UpdatedOn { get; set; }

		public ActiveUnitRoleResultData()
		{
		}

		public ActiveUnitRoleResultData(UnitRoleResultData baseData)
		{
			UnitId = baseData.UnitId;
			UnitRoleId = baseData.UnitRoleId;
			Name = baseData.Name;
		}
	}
}
