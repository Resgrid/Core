
namespace Resgrid.Web.Services.Controllers.Version3.Models.Devices
{
	/// <summary>
	/// Object that contains the device specific information needed to register the device for push notifications
	/// </summary>
	public class DeviceRegistrationInput
	{
		/// <summary>
		/// The platform this device registration is going against
		/// </summary>
		public int Plt { get; set; }

		/// <summary>
		/// The DeviceId to register with Resgrid for Push Notifications
		/// </summary>
		public string Did { get; set; }

		/// <summary>
		/// The PushUri (if needed by the target platform) for the device
		/// </summary>
		public string Uri { get; set; }

		/// <summary>
		/// The UnitId of the device being registered if it's from the Unit App
		/// </summary>
		public int Uid { get; set; }

		/// <summary>
		/// Device UDID
		/// </summary>
		public string Id { get; set; }
	}
}
