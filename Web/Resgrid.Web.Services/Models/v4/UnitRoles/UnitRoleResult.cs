namespace Resgrid.Web.Services.Models.v4.UnitRoles
{
	/// <summary>
	/// A unit role in the Resgrid system
	/// </summary>
	public class UnitRoleResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public UnitRoleResultData Data { get; set; }
	}

	/// <summary>
	/// A unit role
	/// </summary>
	public class UnitRoleResultData
	{
		/// <summary>
		/// Unit Identification number this role belongs to
		/// </summary>
		public string UnitId { get; set; }

		/// <summary>
		/// Unit Roles Identification number
		/// </summary>
		public string UnitRoleId { get; set; }

		/// <summary>
		/// Name of the Unit Role
		/// </summary>
		public string Name { get; set; }
	}
}
