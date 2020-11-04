namespace Resgrid.Web.Services.Controllers.Version3.Models.Status
{
	/// <summary>
	/// Object inputs for setting a users Status/Action. If this object is used in an operation that sets
	/// a status for the current user the UserId value in this object will be ignored.
	/// </summary>
	public class StatusInput
	{
		/// <summary>
		/// UserId (GUID/UUID) of the User to set. This field will be ignored if the input is used on a 
		/// function that is setting status for the current user.
		/// </summary>
		public string Uid { get; set; }

		/// <summary>
		/// The ActionType/Status of the user to set for the user.
		/// </summary>
		public int Typ { get; set; }

		/// <summary>
		/// Responding to Id
		/// </summary>
		public int Rto { get; set; }

		/// <summary>
		/// Geolocation coordinates
		/// </summary>
		public string Geo { get; set; }

		/// <summary>
		/// Destination type (Station = 1 or Call = 2)
		/// </summary>

		public int Dtp { get; set; }

		/// <summary>
		/// Note
		/// </summary>

		public string Not { get; set; }
	}
}
