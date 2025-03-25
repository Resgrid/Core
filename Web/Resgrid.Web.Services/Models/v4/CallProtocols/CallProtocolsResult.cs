using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.CallProtocols
{
	/// <summary>
	/// Gets all the call protocols for the department
	/// </summary>
	public class CallProtocolsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<CallProtocolResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public CallProtocolsResult()
		{
			Data = new List<CallProtocolResultData>();
		}
	}
}
