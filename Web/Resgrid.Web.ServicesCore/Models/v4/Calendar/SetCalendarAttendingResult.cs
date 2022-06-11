namespace Resgrid.Web.Services.Models.v4.Calendar;

/// <summary>
/// The result of attending a calendar event
/// </summary>
public class SetCalendarAttendingResult: StandardApiResponseV4Base
{
	/// <summary>
	/// Identifier of the new attending calendar entry
	/// </summary>
	public string Id { get; set; }
}
