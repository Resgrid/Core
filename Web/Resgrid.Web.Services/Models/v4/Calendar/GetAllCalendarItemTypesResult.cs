using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Calendar;

/// <summary>
/// Result containing all calendar item types
/// </summary>
public class GetAllCalendarItemTypesResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public List<GetAllCalendarItemTypesResultData> Data { get; set; }
}

/// <summary>
/// Data about a calendar item type
/// </summary>
public class GetAllCalendarItemTypesResultData
{
	/// <summary>
	/// Identifier
	/// </summary>
	public string CalendarItemTypeId { get; set; }

	/// <summary>
	/// Name of the type
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Color for this type
	/// </summary>
	public string Color { get; set; }
}
