using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Dispatch
{
	/// <summary>
	/// Data needed for the Dispatch App Modal that sets the state for a unit
	/// </summary>
	public class GetRolesForCallGridResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<GetRolesForCallGridResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public GetRolesForCallGridResult()
		{
			Data = new List<GetRolesForCallGridResultData>();
		}
	}

	/// <summary>
	/// All the data required to populate the New Call form
	/// </summary>
	public class GetRolesForCallGridResultData
	{
		/// <summary>
		/// Role id
		/// </summary>
		public string RoleId { get; set; }

		/// <summary>
		/// Role name
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Number of users in the role
		/// </summary>
		public int Count { get; set; }
	}
}
