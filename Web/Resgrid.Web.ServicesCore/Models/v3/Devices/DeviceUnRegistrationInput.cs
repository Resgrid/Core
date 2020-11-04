namespace Resgrid.Web.Services.Controllers.Version3.Models.Devices
{
	/// <summary>
	/// Unput to deregister a device from Resgrid to stop pushing messages
	/// </summary>
	public class DeviceUnRegistrationInput
	{
		/// <summary>
		/// The Device Id for push registration
		/// </summary>
		public string Did { get; set; }

		/// <summary>
		/// The Push Id (originally from Resgrid) for the registration
		/// </summary>
		public int Pid { get; set; }
	}
}
