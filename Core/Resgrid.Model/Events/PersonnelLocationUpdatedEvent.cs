namespace Resgrid.Model.Events
{
	public class PersonnelLocationUpdatedEvent
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public decimal Latitude { get; set; }
		public decimal Longitude { get; set; }
		public string RecordId { get; set; }

		/// <summary>
		/// UTC time of the location fix. Queue consumers can process pings out of order, so realtime
		/// clients compare this to drop a fix older than the one they already show. Null from producers
		/// that predate the field.
		/// </summary>
		public System.DateTime? Timestamp { get; set; }
	}
}
