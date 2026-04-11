using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.WeatherAlerts
{
	public class WeatherAlertSettingsData
	{
		public bool WeatherAlertsEnabled { get; set; }
		public int MinimumSeverity { get; set; }
		public int AutoMessageSeverity { get; set; }
		public bool CallIntegrationEnabled { get; set; }
		public List<WeatherAlertSeverityScheduleData> AutoMessageSchedule { get; set; }
	}

	public class WeatherAlertSeverityScheduleData
	{
		public int Severity { get; set; }
		public bool Enabled { get; set; }
		public int StartHour { get; set; }
		public int EndHour { get; set; }
	}
}
