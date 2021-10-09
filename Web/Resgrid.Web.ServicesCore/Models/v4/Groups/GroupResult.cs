namespace Resgrid.Web.Services.Models.v4.Groups
{
	/// <summary>
	/// A group in the Resgrid system
	/// </summary>
	public class GroupResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public GroupResultData Data { get; set; }
	}

	/// <summary>
	/// All the data required to populate the New Call form
	/// </summary>
	public class GroupResultData
	{
		/// <summary>
		/// Id of the group
		/// </summary>
		public string GroupId { get; set; }

		/// <summary>
		/// Type id of the Group (Station or Orginizational)
		/// </summary>
		public string TypeId { get; set; }

		/// <summary>
		/// Name of the Group
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Address of the Group (for Station Groups)
		/// </summary>
		public string Address { get; set; }
	}
}
