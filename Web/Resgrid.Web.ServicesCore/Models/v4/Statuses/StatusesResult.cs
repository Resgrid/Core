using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Statuses
{
	/// <summary>
	/// Multiple status (custom states) result
	/// </summary>
	public class StatusesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<StatusResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public StatusesResult()
		{
			Data = new List<StatusResultData>();
		}
	}
}
