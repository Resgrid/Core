using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Shifts
{
	/// <summary>
	/// A single shift
	/// </summary>
	public class GetShiftResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public ShiftsResultData Data { get; set; }
	}
}
