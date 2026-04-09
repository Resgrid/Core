using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.WeatherAlerts
{
	public class GetWeatherAlertZonesResult : StandardApiResponseV4Base
	{
		public List<WeatherAlertZoneResultData> Data { get; set; }

		public GetWeatherAlertZonesResult()
		{
			Data = new List<WeatherAlertZoneResultData>();
		}
	}
}
