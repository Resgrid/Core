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
	}
}
