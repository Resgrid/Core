using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.CallFiles
{
	/// <summary>
	/// A Call file result
	/// </summary>
	public class CallFilesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Data payload
		/// </summary>
		public List<CallFileResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public CallFilesResult()
		{
			Data = new List<CallFileResultData>();
		}
	}
}
