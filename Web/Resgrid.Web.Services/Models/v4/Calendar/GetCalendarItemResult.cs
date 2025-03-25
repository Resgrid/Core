using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Calendar;

/// <summary>
/// Result containing a single calendar items
/// </summary>
public class GetCalendarItemResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public GetAllCalendarItemResultData Data { get; set; }
}
