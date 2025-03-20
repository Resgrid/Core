using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Personnel
{
	/// <summary>
	/// Result that contains all the options available to filter personnel against compatible Resgrid APIs
	/// </summary>
	public class GetPersonnelFilterOptionsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<FilterResult> Data { get; set; }
	}
}
