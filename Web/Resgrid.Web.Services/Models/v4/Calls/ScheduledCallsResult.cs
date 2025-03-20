using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Gets the calls current scheduled but not yet dispatched
	/// </summary>
	public class ScheduledCallsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<CallResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public ScheduledCallsResult()
		{
			Data = new List<CallResultData>();
		}
	}
}
