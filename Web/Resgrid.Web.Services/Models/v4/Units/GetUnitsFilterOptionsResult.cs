using Resgrid.Web.Services.Models.v4.Personnel;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Units
{
	/// <summary>
	/// Result that contains all the options available to filter units against compatible Resgrid APIs
	/// </summary>
	public class GetUnitsFilterOptionsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<FilterResult> Data { get; set; }
	}
}
