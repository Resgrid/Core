namespace Resgrid.Web.Services.Models.v4.Units
{
	/// <summary>
	/// Unit role information for roles on a unit
	/// </summary>
	public class UnitRoleData
	{
		/// <summary>
		/// Unit Role Id
		/// </summary>
		public string RoleId { get; set; }

		/// <summary>
		/// User Id of the user in the role (could be null)
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// Name of the Role
		/// </summary>
		public string RoleName { get; set; }

		/// <summary>
		/// Name of the user in the role (could be null)
		/// </summary>
		public string Name { get; set; }
	}
}
