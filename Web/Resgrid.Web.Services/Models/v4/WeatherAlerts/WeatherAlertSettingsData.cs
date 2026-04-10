namespace Resgrid.Web.Services.Models.v4.WeatherAlerts
{
	public class WeatherAlertSettingsData
	{
		public bool WeatherAlertsEnabled { get; set; }
		public int MinimumSeverity { get; set; }
		public int AutoMessageSeverity { get; set; }
		public bool CallIntegrationEnabled { get; set; }
	}
}
