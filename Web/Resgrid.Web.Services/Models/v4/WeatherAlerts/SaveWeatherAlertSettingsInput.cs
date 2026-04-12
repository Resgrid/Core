using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.WeatherAlerts
{
	public class SaveWeatherAlertSettingsInput
	{
		public bool WeatherAlertsEnabled { get; set; }
		public int MinimumSeverity { get; set; }
		public int AutoMessageSeverity { get; set; }
		public bool CallIntegrationEnabled { get; set; }
		public List<WeatherAlertSeverityScheduleData> AutoMessageSchedule { get; set; }
		public string ExcludedEvents { get; set; }
	}
}
