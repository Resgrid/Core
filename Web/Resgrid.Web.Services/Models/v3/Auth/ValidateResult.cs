namespace Resgrid.Web.Services.Controllers.Version3.Models.Auth
{
	/// <summary>
	/// The Response from the server regarding a token generation request.
	/// </summary>
	public class ValidateResult
	{
		/// <summary>
		/// Auth token if authentication and validation was successful
		/// </summary>
		public string Tkn { get; set; }

		/// <summary>
		/// Timestamp of when this token expires
		/// </summary>
		public string Txd { get; set; }

		/// <summary>
		/// If the user settings are successful validated this will be populated by their full name 
		/// </summary>
		public string Nme { get; set; }

		/// <summary>
		/// If the user settings are successful validated this will be populated by their email address
		/// </summary>
		public string Eml { get; set; }

		/// <summary>
		/// UserId of the current user
		/// </summary>
		public string Uid { get; set; }

		/// <summary>
		/// Timestamp (in Unix time) of when the department was created
		/// </summary>
		public string Dcd { get; set; }

		/// <summary>
		/// Name of the department
		/// </summary>
		public string Dnm { get; set; }

		/// <summary>
		/// Department Id
		/// </summary>
		public int Did { get; set; }
	}
}
