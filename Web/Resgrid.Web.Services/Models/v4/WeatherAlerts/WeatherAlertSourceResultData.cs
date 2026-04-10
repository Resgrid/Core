namespace Resgrid.Web.Services.Models.v4.WeatherAlerts
{
	public class WeatherAlertSourceResultData
	{
		public string WeatherAlertSourceId { get; set; }
		public int DepartmentId { get; set; }
		public string Name { get; set; }
		public int SourceType { get; set; }
		public string AreaFilter { get; set; }
		public bool HasApiKey { get; set; }
		public string CustomEndpoint { get; set; }
		public int PollIntervalMinutes { get; set; }
		public bool Active { get; set; }
		public string LastPollUtc { get; set; }
		public string LastSuccessUtc { get; set; }
		public bool IsFailure { get; set; }
		public string ErrorMessage { get; set; }
	}
}
