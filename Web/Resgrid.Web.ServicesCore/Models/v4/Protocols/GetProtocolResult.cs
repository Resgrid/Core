using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Protocols
{
	/// <summary>
	/// Result containing all the data required to populate the New Call form
	/// </summary>
	public class GetProtocolResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public ProtocolResultData Data { get; set; }
	}
}
