using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Units
{
	/// <summary>
	/// Multiple Units Result
	/// </summary>
	public class UnitsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<UnitResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public UnitsResult()
		{
			Data = new List<UnitResultData>();
		}
	}
}
