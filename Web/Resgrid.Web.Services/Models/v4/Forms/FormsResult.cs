using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Forms
{
	/// <summary>
	/// Multiple forms result
	/// </summary>
	public class FormsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<FormResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public FormsResult()
		{
			Data = new List<FormResultData>();
		}
	}
}

