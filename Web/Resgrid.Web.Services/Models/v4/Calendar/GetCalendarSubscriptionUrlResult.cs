namespace Resgrid.Web.Services.Models.v4.Calendar;

/// <summary>
/// Result containing the external calendar subscription URL for a user.
/// </summary>
public class GetCalendarSubscriptionUrlResult : StandardApiResponseV4Base
{
	/// <summary>
	/// The HTTPS subscription URL to paste into Google Calendar, Outlook, Apple Calendar, etc.
	/// </summary>
	public string SubscriptionUrl { get; set; }

	/// <summary>
	/// The webcal:// version of the URL for direct launch in a calendar application.
	/// </summary>
	public string WebcalUrl { get; set; }

	/// <summary>
	/// The raw encrypted URL-safe token embedded in the subscription URL.
	/// Store this if you need to construct the URL manually.
	/// </summary>
	public string Token { get; set; }
}

