using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.UnitStatus
{
	/// <summary>
	/// Unit statuses (states)
	/// </summary>
	public class UnitStautsesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<UnitStatusResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public UnitStautsesResult()
		{
			Data = new List<UnitStatusResultData>();
		}
	}
}
