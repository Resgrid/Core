using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Groups
{
	/// <summary>
	/// A list of groups in the Resgrid system
	/// </summary>
	public class GroupResults : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<GroupResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public GroupResults()
		{
			Data = new List<GroupResultData>();
		}
	}
}
