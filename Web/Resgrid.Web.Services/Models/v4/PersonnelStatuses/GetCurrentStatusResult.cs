using System;

namespace Resgrid.Web.Services.Models.v4.PersonnelStatuses
{
	/// <summary>
	/// The result of getting the current status (action) for a user
	/// </summary>
	public class GetCurrentStatusResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public GetCurrentStatusResultData Data { get; set; }
	}

	/// <summary>
	/// Information about a User status (action)
	/// </summary>
	public class GetCurrentStatusResultData
	{
		/// <summary>
		/// The UserId GUID/UUID for the user status being return
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// DepartmentId of the deparment the user belongs to
		/// </summary>
		public string DepartmentId { get; set; }

		/// <summary>
		/// The current action/status type for the user
		/// </summary>
		public int StatusType { get; set; }

		/// <summary>
		/// The timestamp of the last action. This is converted UTC version of the timestamp.
		/// </summary>
		public DateTime TimestampUtc { get; set; }

		/// <summary>
		/// The timestamp of the last action. This is converted UTC to the departments, or users, TimeZone.
		/// </summary>
		public DateTime Timestamp { get; set; }

		/// <summary>
		/// The current action/status destination id for the user
		/// </summary>
		public int? DestinationId { get; set; }

		/// <summary>
		/// Destination type for the action log
		/// </summary>
		public int? DestinationType { get; set; }

		/// <summary>
		/// Geolocation for this status
		/// </summary>
		public string GeoLocationData { get; set; }

		/// <summary>
		/// Note for this status
		/// </summary>
		public string Note { get; set; }
	}
}
