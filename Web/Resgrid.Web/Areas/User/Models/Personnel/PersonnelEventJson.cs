namespace Resgrid.Web.Areas.User.Models.Personnel
{
	public class PersonnelEventJson
	{
		public int EventId { get; set; }
		public string UserId { get; set; }
		public string PersonName { get; set; }
		public string State { get; set; }
		public string Timestamp { get; set; }
		public string LocalTimestamp { get; set; }
		public string DestinationName { get; set; }
		public string Latitude { get; set; }
		public string Longitude { get; set; }
		public string Note { get; set; }
	}
}

