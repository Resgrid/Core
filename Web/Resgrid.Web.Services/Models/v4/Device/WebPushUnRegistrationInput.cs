namespace Resgrid.Web.Services.Models.v4.Device
{
	/// <summary>
	/// Takes a browser or desktop push token off the web push channel it was registered on (RegisterDevice or
	/// RegisterUnitDevice with Platform 3). Sent in the body rather than the query string so the token never
	/// lands in request logs.
	/// </summary>
	public class WebPushUnRegistrationInput
	{
		/// <summary>
		/// The FCM web token the browser or desktop app registered
		/// </summary>
		public string Token { get; set; }

		/// <summary>
		/// The department code the token was registered under; empty means the caller's active department
		/// </summary>
		public string Prefix { get; set; }

		/// <summary>
		/// Source app of the registration ("IC" for the Incident Command app); null/empty means the user's default subscriber
		/// </summary>
		public string Source { get; set; }

		/// <summary>
		/// The unit the token was registered for (Unit app); empty for a user registration
		/// </summary>
		public string UnitId { get; set; }
	}
}
