using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.CallTypes
{
	/// <summary>
	/// Gets the call types
	/// </summary>
	public class CallTypesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<CallTypeResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public CallTypesResult()
		{
			Data = new List<CallTypeResultData>();
		}
	}
}
