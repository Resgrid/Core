namespace Resgrid.Web.Services.Models.v4.Device
{
	/// <summary>
	/// Depicts a request to register for push notifications
	/// </summary>
	public class PushRegistrationResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Id of the device registration created
		/// </summary>
		public string Id { get; set; }
	}
}
