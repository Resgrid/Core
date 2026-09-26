namespace Resgrid.Model.Events
{
	public class UnitLocationUpdatedEvent
	{
		public int DepartmentId { get; set; }
		public string UnitId { get; set; }
		public double Latitude { get; set; }
		public double Longitude { get; set; }
		public string RecordId { get; set; }

		/// <summary>
		/// UTC time of the location fix. Trackers replay buffered fixes and queue consumers can process
		/// them out of order, so realtime clients compare this to drop a fix older than the one they
		/// already show. Null from producers that predate the field.
		/// </summary>
		public System.DateTime? Timestamp { get; set; }
	}
}
