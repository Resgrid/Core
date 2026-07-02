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

		/// <summary>
		/// Optional personnel role (qualification) required to fill this unit role. Null/empty if none.
		/// </summary>
		public string PersonnelRoleId { get; set; }

		/// <summary>
		/// Display name of the required personnel role, if any.
		/// </summary>
		public string PersonnelRoleName { get; set; }

		/// <summary>
		/// When true the personnel role is a hard requirement (unqualified members are blocked from being
		/// assigned). When false it is preferred (allowed, but the unit is reported as degraded).
		/// </summary>
		public bool PersonnelRoleRequired { get; set; }
	}
}
