namespace Resgrid.Web.Services.Controllers.Version3.Models.Devices
{
	/// <summary>
	/// The result object for device registration requires, or deregistration requests.
	/// </summary>
	public class DeviceRegistrationResult
	{
		/// <summary>
		/// Id of the device registration created
		/// </summary>
		public int Id { get; set; }

		/// <summary>
		/// Platform type of the registration
		/// </summary>
		public string Typ { get; set; }

		/// <summary>
		/// Was the operational successful
		/// </summary>
		public bool Sfl { get; set; }
	}
}
