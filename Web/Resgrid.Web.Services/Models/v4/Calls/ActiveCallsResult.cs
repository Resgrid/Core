using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Gets the calls current active, been dispatched and not closed or deleted
	/// </summary>
	public class ActiveCallsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<CallResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public ActiveCallsResult()
		{
			Data = new List<CallResultData>();
		}
	}
}
