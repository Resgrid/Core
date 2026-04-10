namespace Resgrid.Web.Services.Models.v4.WeatherAlerts
{
	public class SaveWeatherAlertSourceInput
	{
		public string WeatherAlertSourceId { get; set; }
		public string Name { get; set; }
		public int SourceType { get; set; }
		public string AreaFilter { get; set; }
		public string ApiKey { get; set; }
		public string CustomEndpoint { get; set; }
		public int PollIntervalMinutes { get; set; }
		public bool Active { get; set; }
	}
}
