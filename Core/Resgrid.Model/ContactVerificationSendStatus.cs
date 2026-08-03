namespace Resgrid.Model
{
	/// <summary>
	/// Describes the outcome of a request to send a contact verification code.
	/// </summary>
	public enum ContactVerificationSendStatus
	{
		Sent = 0,
		ContactNotConfigured = 1,
		InvalidContact = 2,
		RateLimited = 3,
		DeliveryFailed = 4
	}
}
