namespace Resgrid.Web.Services.Controllers.Version3.Models.Units
{
	/// <summary>
	/// Contains a role for a unit
	/// </summary>
	public class UnitRoleResult
	{
		/// <summary>
		/// Unit Identification number this role belongs to
		/// </summary>
		public int UnitId { get; set; }

		/// <summary>
		/// Unit Roles Identification number
		/// </summary>
		public int UnitRoleId { get; set; }

		/// <summary>
		/// Name of the Unit Role
		/// </summary>
		public string Name { get; set; }
	}
}
