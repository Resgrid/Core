namespace Resgrid.Web.Services.Models.v4.Roles
{
	/// <summary>
	/// A role in the Resgrid system
	/// </summary>
	public class RoleResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public RoleResultData Data { get; set; }
	}

	/// <summary>
	/// Role
	/// </summary>
	public class RoleResultData
	{
		/// <summary>
		/// Id of the Role
		/// </summary>
		public string RoleId { get; set; }

		/// <summary>
		/// Name of the Role
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Absolute URL of the role's uploaded map icon (a 128 px square PNG, anonymous GET, immutable), or null. A person's
		/// map pin uses the icon of the first role they hold, by <see cref="SortOrder"/>, that has one.
		/// </summary>
		public string IconUrl { get; set; }

		/// <summary>
		/// The role's position in the department's role order (lowest first; ties order by name). 0 until the department
		/// orders its roles.
		/// </summary>
		public int SortOrder { get; set; }
	}
}
