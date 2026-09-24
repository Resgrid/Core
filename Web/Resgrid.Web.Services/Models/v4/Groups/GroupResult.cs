namespace Resgrid.Web.Services.Models.v4.Groups
{
	/// <summary>
	/// A group in the Resgrid system
	/// </summary>
	public class GroupResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public GroupResultData Data { get; set; }
	}

	/// <summary>
	/// All the data required to populate the New Call form
	/// </summary>
	public class GroupResultData
	{
		/// <summary>
		/// Id of the group
		/// </summary>
		public string GroupId { get; set; }

		/// <summary>
		/// Type id of the Group (Station or Orginizational)
		/// </summary>
		public string TypeId { get; set; }

		/// <summary>
		/// Name of the Group
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Address of the Group (for Station Groups)
		/// </summary>
		public string Address { get; set; }

		/// <summary>
		/// Id of the parent group (e.g. the service area a station sits under); empty for top-level groups
		/// </summary>
		public string ParentGroupId { get; set; }

		/// <summary>
		/// Stored latitude of the group's location (Station Groups), if any
		/// </summary>
		public string Latitude { get; set; }

		/// <summary>
		/// Stored longitude of the group's location (Station Groups), if any
		/// </summary>
		public string Longitude { get; set; }

		/// <summary>
		/// The group's boundary as a JSON array of {"lat":..,"lng":..} points: a station's response area or an
		/// organizational group's service area. Empty when the group has no boundary.
		/// </summary>
		public string Geofence { get; set; }

		/// <summary>
		/// Display color for the boundary
		/// </summary>
		public string GeofenceColor { get; set; }
	}
}
