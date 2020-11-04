using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Units
{
	/// <summary>
	/// A Status result for a Unit in a department
	/// </summary>
	public class UnitStatusResult
	{
		/// <summary>
		/// The integer based Unit Identifier
		/// </summary>
		public int Uid { get; set; }

		/// <summary>
		/// The Id of the Destination for the Unit (could be a Call or a Station)
		/// </summary>
		public int Did { get; set; }

		/// <summary>
		/// The current status/state of the Unit
		/// </summary>
		public int Typ { get; set; }

		/// <summary>
		/// The Timestamp of the status
		/// </summary>
		public DateTime Tmp { get; set; }
	}
}
