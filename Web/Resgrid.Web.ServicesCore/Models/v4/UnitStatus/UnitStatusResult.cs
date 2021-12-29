using System;

namespace Resgrid.Web.Services.Models.v4.UnitStatus
{
	/// <summary>
	/// Depicts a unit status in the Resgrid system.
	/// </summary>
	public class UnitStatusResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public UnitStatusResultData Data { get; set; }
	}

	/// <summary>
	/// Depicts a unit's status
	/// </summary>
	public class UnitStatusResultData
	{
		/// <summary>
		/// Unit Id
		/// </summary>
		public string UnitId { get; set; }

		/// <summary>
		/// Units Name
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// The Type of the Unit
		/// </summary>
		public string Type { get; set; }

		/// <summary>
		/// Units current Status (State)
		/// </summary>
		public string State { get; set; }

		/// <summary>
		/// CSS for status (for display)
		/// </summary>
		public string StateCss { get; set; }

		/// <summary>
		/// CSS Style for status (for display)
		/// </summary>
		public string StateStyle { get; set; }

		/// <summary>
		/// Timestamp of this Unit State
		/// </summary>
		public DateTime Timestamp { get; set; }

		/// <summary>
		/// Timestamp in Utc of this Unit State
		/// </summary>
		public DateTime TimestampUtc { get; set; }

		/// <summary>
		/// Destination Id (Station or Call)
		/// </summary>
		public int? DestinationId { get; set; }

		/// <summary>
		/// Name of the Desination (Call or Station)
		/// </summary>
		public string DestinationName { get; set; }

		/// <summary>
		/// Note for the State
		/// </summary>
		public string Note { get; set; }

		/// <summary>
		/// Latitude
		/// </summary>
		public decimal? Latitude { get; set; }

		/// <summary>
		/// Longitude
		/// </summary>
		public decimal? Longitude { get; set; }

		/// <summary>
		/// Name of the Group the Unit is in
		/// </summary>
		public string GroupName { get; set; }

		/// <summary>
		/// Id of the Group the Unit is in
		/// </summary>
		public int GroupId { get; set; }
	}
}
