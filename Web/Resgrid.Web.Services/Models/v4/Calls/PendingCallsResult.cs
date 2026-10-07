using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// The department's pending calls: saved but not yet dispatched, oldest first
	/// </summary>
	public class PendingCallsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<CallResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public PendingCallsResult()
		{
			Data = new List<CallResultData>();
		}
	}
}
