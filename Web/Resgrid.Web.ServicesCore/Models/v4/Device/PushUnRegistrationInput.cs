namespace Resgrid.Web.Services.Models.v4.Device
{
	/// <summary>
	/// Unregister a device for push
	/// </summary>
	public class PushUnRegistrationInput
	{
		/// <summary>
		/// Device UDID
		/// </summary>
		public string DeviceUuid { get; set; }
	}
}
