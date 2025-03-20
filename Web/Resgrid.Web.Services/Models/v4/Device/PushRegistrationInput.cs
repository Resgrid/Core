namespace Resgrid.Web.Services.Models.v4.Device
{
	/// <summary>
	/// Object that contains the device specific information needed to register the device for push notifications
	/// </summary>
	public class PushRegistrationInput
	{
		/// <summary>
		/// The platform this device registration is going against
		/// </summary>
		public int Platform { get; set; }

		/// <summary>
		/// The push network resgistration token to register with Resgrid for Push Notifications
		/// </summary>
		public string Token { get; set; }

		/// <summary>
		/// The UnitId of the device being registered if it's from the Unit App
		/// </summary>
		public string UnitId { get; set; }

		/// <summary>
		/// Device UDID
		/// </summary>
		public string DeviceUuid { get; set; }

		/// <summary>
		/// Prefix
		/// </summary>
		public string Prefix { get; set; }
	}
}
