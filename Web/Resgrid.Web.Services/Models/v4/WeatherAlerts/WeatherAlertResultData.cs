namespace Resgrid.Web.Services.Models.v4.WeatherAlerts
{
	public class WeatherAlertResultData
	{
		public string WeatherAlertId { get; set; }
		public int DepartmentId { get; set; }
		public string WeatherAlertSourceId { get; set; }
		public string ExternalId { get; set; }
		public string Sender { get; set; }
		public string Event { get; set; }
		public int AlertCategory { get; set; }
		public int Severity { get; set; }
		public int Urgency { get; set; }
		public int Certainty { get; set; }
		public int Status { get; set; }
		public string Headline { get; set; }
		public string Description { get; set; }
		public string Instruction { get; set; }
		public string AreaDescription { get; set; }
		public string Polygon { get; set; }
		public string Geocodes { get; set; }
		public string CenterGeoLocation { get; set; }
		public string OnsetUtc { get; set; }
		public string ExpiresUtc { get; set; }
		public string EffectiveUtc { get; set; }
		public string SentUtc { get; set; }
		public string FirstSeenUtc { get; set; }
		public string LastUpdatedUtc { get; set; }
		public string ReferencesExternalId { get; set; }
		public bool NotificationSent { get; set; }
		public int? SystemMessageId { get; set; }
	}
}
