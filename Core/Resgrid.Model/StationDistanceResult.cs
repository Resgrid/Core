namespace Resgrid.Model
{
	/// <summary>
	/// A station group resolved against a reference point (usually a call location):
	/// its usable coordinates, straight-line distance and whether its geofence
	/// contains the point. Produced by IGeoService station helpers for the dispatch
	/// recommendation engine.
	/// </summary>
	public class StationDistanceResult
	{
		public DepartmentGroup Station { get; set; }

		public double Latitude { get; set; }

		public double Longitude { get; set; }

		/// <summary>Straight-line meters from the reference point to the station's coordinates.</summary>
		public double DistanceMeters { get; set; }

		/// <summary>True when the station's geofence polygon contains the reference point.</summary>
		public bool ContainsPoint { get; set; }

		/// <summary>True when the station has a parseable geofence polygon.</summary>
		public bool HasGeofence { get; set; }
	}
}
