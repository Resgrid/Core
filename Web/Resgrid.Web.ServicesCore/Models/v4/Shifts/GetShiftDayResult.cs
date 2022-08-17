using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Shifts
{
	/// <summary>
	/// A single Shift day result
	/// </summary>
	public class GetShiftDayResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public ShiftDayResultData Data { get; set; }
	}
}
