using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.CustomStatuses
{
	/// <summary>
	/// Custom defined Status for Personnel and Units
	/// </summary>
	public class CustomStatusesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<CustomStatusResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public CustomStatusesResult()
		{
			Data = new List<CustomStatusResultData>();
		}
	}
}
