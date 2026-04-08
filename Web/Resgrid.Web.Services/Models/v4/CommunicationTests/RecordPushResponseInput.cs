namespace Resgrid.Web.Services.Models.v4.CommunicationTests;

/// <summary>
/// Input for recording a push notification response
/// </summary>
public class RecordPushResponseInput
{
	/// <summary>
	/// The response token from the push notification
	/// </summary>
	public string ResponseToken { get; set; }
}

/// <summary>
/// Result of recording a push notification response
/// </summary>
public class RecordPushResponseResult : StandardApiResponseV4Base
{
}
