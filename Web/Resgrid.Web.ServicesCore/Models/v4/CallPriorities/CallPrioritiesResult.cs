using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.CallPriorities
{
	/// <summary>
	/// Gets all the call priorities for the department
	/// </summary>
	public class CallPrioritiesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<CallPriorityResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public CallPrioritiesResult()
		{
			Data = new List<CallPriorityResultData>();
		}
	}
}
