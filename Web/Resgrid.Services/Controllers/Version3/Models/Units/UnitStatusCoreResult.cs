using Resgrid.Model;
using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Units
{
	/// <summary>
	/// A Status result for a Unit in a department
	/// </summary>
	public class UnitStatusCoreResult
	{
		/// <summary>
		/// The integer based Unit Identifier
		/// </summary>
		public int UnitId { get; set; }

		/// <summary>
		/// The Name of the Unit
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// The Type of the unit as defined in the department
		/// </summary>
		public string Type { get; set; }

		/// <summary>
		/// The Station Department Group name for the unit
		/// </summary>
		public string Station { get; set; }

		/// <summary>
		/// The Id of the Destination for the Unit (could be a Call or a Station)
		/// </summary>
		public int DestinationId { get; set; }

		/// <summary>
		/// The current status/state of the Unit
		/// </summary>
		public UnitStateTypes StateType { get; set; }

		public int StateTypeId { get; set; }

		/// <summary>
		/// The Timestamp of the status
		/// </summary>
		public DateTime Timestamp { get; set; }

		public DateTime? LocalTimestamp { get; set; }

		public string Note { get; set; }

		public decimal? Latitude { get; set; }

		public decimal? Longitude { get; set; }
	}
}
