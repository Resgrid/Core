using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.WeatherAlerts
{
	public class GetActiveWeatherAlertsResult : StandardApiResponseV4Base
	{
		public List<WeatherAlertResultData> Data { get; set; }

		public GetActiveWeatherAlertsResult()
		{
			Data = new List<WeatherAlertResultData>();
		}
	}
}
