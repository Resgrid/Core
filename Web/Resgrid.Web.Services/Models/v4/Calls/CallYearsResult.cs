using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>The years in which the department has calls, newest first.</summary>
	public class CallYearsResult : StandardApiResponseV4Base
	{
		/// <summary>Four-digit years as strings.</summary>
		public List<string> Data { get; set; } = [];
	}
}
