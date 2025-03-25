using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Dispatch
{
	/// <summary>
	/// Data needed for the new call form to display dispatch groups
	/// </summary>
	public class GetGroupsForCallGridResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<GetGroupsForCallGridResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public GetGroupsForCallGridResult()
		{
			Data = new List<GetGroupsForCallGridResultData>();
		}
	}

	/// <summary>
	/// All the data required to populate the New Call form
	/// </summary>
	public class GetGroupsForCallGridResultData
	{
		/// <summary>
		/// Group id
		/// </summary>
		public string GroupId { get; set; }

		/// <summary>
		/// Group name
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Members count
		/// </summary>
		public int Count { get; set; }
	}
}
